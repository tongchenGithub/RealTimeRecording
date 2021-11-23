using System;
using System.Collections;
using System.Diagnostics;
using System.Runtime.InteropServices;
using UnityEngine;

[ExecuteInEditMode]
public class FFRecorder : MonoBehaviour
{
    #region 模拟控制台信号需要使用的DLL

    [DllImport("kernel32.dll")]
    static extern bool GenerateConsoleCtrlEvent(int dwCtrlEvent, int dwProcessGroupId);

    [DllImport("kernel32.dll")]
    static extern bool SetConsoleCtrlHandler(IntPtr handlerRoutine, bool add);

    [DllImport("kernel32.dll")]
    static extern bool AttachConsole(int dwProcessId);

    [DllImport("kernel32.dll")]
    static extern bool FreeConsole();

    #endregion

    #region 设置菜单

    public enum RecordType
    {
        GDIGRAB,
        DSHOW
    }

    public enum Bitrate
    {
        _1000k,
        _1500k,
        _2000k,
        _2500k,
        _3000k,
        _3500k,
        _4000k,
        _5000k,
        _8000k
    }

    public enum Framerate
    {
        _14,
        _24,
        _30,
        _45,
        _60
    }

    public enum Resolution
    {
        _1280x720,
        _1920x1080,
        _Auto
    }

    public enum OutputPath
    {
        Desktop,
        StreamingAsset,
        DataPath,
        Custom
    }

    #endregion

    #region 成员

    [Tooltip("启用Debug则显示CMD窗口，否则不显示。")] [SerializeField]
    private bool _debug = false;

    [Tooltip("DSHOW - 录制全屏 \nGUIGRAB - 录制游戏窗口（仅用于发布版）")]
    public RecordType recordType = RecordType.DSHOW;

    public Resolution resolution = Resolution._1280x720;
    public Framerate framerate = Framerate._24;
    public Bitrate bitrate = Bitrate._1500k;
    public OutputPath outputPath = OutputPath.Desktop;
    public string customOutputPath = @"D:/Records";

    public bool IsRecording
    {
        get { return _isRecording; }
    }

    /** ffmpeg参数说明
     * -f ：格式
     *     gdigrab ：ffmpeg内置的用于抓取Windows桌面的方法，支持抓取指定名称的窗口
     *     dshow ：依赖于第三方软件Screen Capture Recorder（后面简称SCR）
     * -i ：输入源
     *     title ：要录制的窗口的名称，仅用于GDIGRAB方式
     *     video ：视频播放硬件名称或者"screen-capture-recorder"，后者依赖SCR
     *     audio ：音频播放硬件名称或者"virtual-audio-capturer"，后者依赖SCR
     * -preset ultrafast ：以最快的速度进行编码，生成的视频文件大
     * -c:v ：视频编码方式
     * -c:a ：音频编码方式
     * -b:v ：视频比特率
     * -r ：视频帧率
     * -s ：视频分辨率
     * -y ：输出文件覆盖已有文件不提示
     * 
     * FFMPEG官方文档：http://ffmpeg.org/ffmpeg-all.html
     * Screen Capture Recorder主页：https://github.com/rdp/screen-capture-recorder-to-video-windows-free
     */

    // 参数：窗口名称 -b比特率 -r帧率 -s分辨率 文件路径 文件名
    private const string FFARGS_GDIGRAB =
        "-f gdigrab -i title={0} -f dshow -i audio=\"virtual-audio-capturer\" -y -preset ultrafast -rtbufsize 3500k -b:v {1} -r {2} -s {3} {4}/{5}.mp4";

    // 参数：-b比特率 -r帧率 -s分辨率 文件路径 文件名
    private const string FFARGS_DSHOW =
        "-f dshow -i video=\"screen-capture-recorder\" -f dshow -i audio=\"virtual-audio-capturer\" -y -preset ultrafast -rtbufsize 3500k -b:v {0} -r {1} -s {2} {3}/{4}.mp4";

    private string _ffpath;
    private string _ffargs;

    private int _pid;
    private bool _isRecording = false;

    #endregion

#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
    private void Start()
    {
    
        _debug = false;
    }
#endif

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private void OnGUI()
    {
        if (GUILayout.Button("Start")) StartRecording();
        if (GUILayout.Button("Stop")) StopRecording(() => { UnityEngine.Debug.Log("结束录制。"); });
    }
#endif

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (_debug) UnityEngine.Debug.Log("FFRecorder - CMD窗口已启用。");
        else UnityEngine.Debug.Log("FFRecorder - CMD窗口已禁用。");

        if (recordType == RecordType.GDIGRAB)
        {
            UnityEngine.Debug.Log("FFRecorder - 使用【GDIGRAB】模式录制当前窗口。");
            UnityEngine.Debug.LogError("FFRecorder - 【GDIGRAB】模式在编辑器中不可用。");
        }
        else if (recordType == RecordType.DSHOW)
        {
            UnityEngine.Debug.Log("FFRecorder - 使用【DSHOW】模式录制全屏。");
        }
    }
#endif

    public void StartRecording()
    {
        if (_isRecording)
        {
            UnityEngine.Debug.LogError("FFRecorder::StartRecording - 当前已有录制进程。");
            return;
        }

        // 杀死已有的ffmpeg进程，不要加.exe后缀
        Process[] goDie = Process.GetProcessesByName("ffmpeg");
        foreach (Process p in goDie) p.Kill();

        // 解析设置，如果设置正确，则开始录制
        bool validSettings = ParseSettings();
        if (validSettings)
        {
            UnityEngine.Debug.Log("FFRecorder::StartRecording - 执行命令：" + _ffpath + " " + _ffargs);
            StartCoroutine(IERecording());
        }
        else
        {
            UnityEngine.Debug.LogError("FFRecorder::StartRecording - 设置不当，录制取消，请检查控制台输出。");
        }
    }

    public void StopRecording(Action _OnStopRecording)
    {
        if (!_isRecording)
        {
            UnityEngine.Debug.LogError("FFRecorder::StopRecording - 当前没有录制进程，已取消操作。");
            return;
        }

        StartCoroutine(IEExitCmd(_OnStopRecording));
    }

    private bool ParseSettings()
    {
        _ffpath = Application.streamingAssetsPath + @"/ffmpeg/ffmpeg.exe";
        string name = Application.productName + "_" + DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss");

        // 分辨率
        string s;
        if (resolution == Resolution._1280x720)
        {
            int w = 1280;
            int h = 720;
            if (Screen.width < w)
            {
                w = Screen.width;
                UnityEngine.Debug.LogWarning(string.Format("录制水平分辨率大于屏幕水平分辨率，已自动缩小为{0}。", w));
            }

            if (Screen.height < h)
            {
                h = Screen.height;
                UnityEngine.Debug.LogWarning(string.Format("录制垂直分辨率大于屏幕垂直分辨率，已自动缩小为{0}。", h));
            }

            s = w.ToString() + "x" + h.ToString();
        }
        else if (resolution == Resolution._1920x1080)
        {
            int w = 1920;
            int h = 1080;
            if (Screen.width < w)
            {
                w = Screen.width;
                UnityEngine.Debug.LogWarning(string.Format("录制水平分辨率大于屏幕水平分辨率，已自动缩小为{0}。", w));
            }

            if (Screen.height < h)
            {
                h = Screen.height;
                UnityEngine.Debug.LogWarning(string.Format("录制垂直分辨率大于屏幕垂直分辨率，已自动缩小为{0}。", h));
            }

            s = w.ToString() + "x" + h.ToString();
        }
        else /*(resolution == Resolution._Auto)*/
        {
            s = Screen.width.ToString() + "x" + Screen.height.ToString();
        }

        // 帧率
        string r = framerate.ToString().Remove(0, 1);
        // 比特率
        string b = bitrate.ToString().Remove(0, 1);

        // 输出位置
        string output;
        if (outputPath == OutputPath.Desktop)
            output = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) + "/" +
                     Application.productName + "_Records";
        else if (outputPath == OutputPath.DataPath)
            output = Application.dataPath + "/" + Application.productName + "_Records";
        else if (outputPath == OutputPath.StreamingAsset)
            output = Application.streamingAssetsPath + "/" + Application.productName + "_Records";
        else /*(outputPath == OutputPath.Custom)*/ output = customOutputPath;

        // 命令行参数
        if (recordType == RecordType.GDIGRAB)
        {
            _ffargs = string.Format(FFARGS_GDIGRAB, Application.productName, b, r, s, output, name);
        }
        else /*(recordType == RecordType.DSHOW)*/
        {
            _ffargs = string.Format(FFARGS_DSHOW, b, r, s, output, name);
        }

        // 创建输出文件夹
        if (!System.IO.Directory.Exists(output))
        {
            try
            {
                System.IO.Directory.CreateDirectory(output);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("FFRecorder::ParseSettings - " + e.Message);
                return false;
            }
        }

        return true;
    }

    // 不一定要用协程
    private IEnumerator IERecording()
    {
        yield return null;

        Process ffp = new Process();
        ffp.StartInfo.FileName = _ffpath; // 进程可执行文件位置
        ffp.StartInfo.Arguments = _ffargs; // 传给可执行文件的命令行参数
        ffp.StartInfo.CreateNoWindow = !_debug; // 是否显示控制台窗口
        ffp.StartInfo.UseShellExecute = _debug; // 是否使用操作系统Shell程序启动进程
        ffp.Start(); // 开始进程

        _pid = ffp.Id;
        _isRecording = true;
    }

    private IEnumerator IEExitCmd(Action _OnStopRecording)
    {
        // 将当前进程附加到pid进程的控制台
        AttachConsole(_pid);
        // 将控制台事件的处理句柄设为Zero，即当前进程不响应控制台事件
        // 避免在向控制台发送【Ctrl C】指令时连带当前进程一起结束
        SetConsoleCtrlHandler(IntPtr.Zero, true);
        // 向控制台发送 【Ctrl C】结束指令
        // ffmpeg会收到该指令停止录制
        GenerateConsoleCtrlEvent(0, 0);

        // ffmpeg不能立即停止，等待一会，否则视频无法播放
        yield return new WaitForSeconds(3.0f);

        // 卸载控制台事件的处理句柄，不然之后的ffmpeg调用无法正常停止
        SetConsoleCtrlHandler(IntPtr.Zero, false);
        // 剥离已附加的控制台
        FreeConsole();

        _isRecording = false;

        if (_OnStopRecording != null)
        {
            _OnStopRecording();
        }
    }

    // 程序结束时要杀掉后台的录制进程，但这样会导致输出文件无法播放
    private void OnDestroy()
    {
        if (_isRecording)
        {
            try
            {
                UnityEngine.Debug.LogError("FFRecorder::OnDestroy - 录制进程非正常结束，输出文件可能无法播放。");
                Process.GetProcessById(_pid).Kill();
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("FFRecorder::OnDestroy - " + e.Message);
            }
        }
    }
}