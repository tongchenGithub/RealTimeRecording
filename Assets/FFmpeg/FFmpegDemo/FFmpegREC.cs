using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ionic.Zip;
using UnityEditor;
using UnityEditor.Media;
using UnityEngine;
using UnityEngine.Collections;
using Debug = UnityEngine.Debug;
using FileMode = System.IO.FileMode;

namespace FFmpeg.Demo.REC
{
    [RequireComponent(typeof(RecMicAudio), typeof(RecSystemAudio))]
    public class FFmpegREC : MonoBehaviour, IFFmpegHandler
    {
        public ComputeShader shader;

        // ComputeBuffer, c#端与gpu数据通信的容器，我们组织好需要计算的数据，
        // 装在这个buffer里面，然后把这个buffer塞到computeShader里面，为gpu计算做数据准备
        private ComputeBuffer buffer;
        private Texture2D texture;
        public RenderTexture renderTexture;
        private uint[] pixels;
        private byte[] bytes;
        int width = Screen.width, height = Screen.height;

        private int kernel;

        //Data
        [Header("Targeted FPS")] public int FPS = 30;
        float actualFPS;

        [Header("Change before initialization")]
        public RecAudioSource recAudioSource = RecAudioSource.System;

        Rect camRect;

        //References
        IRecAudio soundRecorder;
        Texture2D frameBuffer;

        private const int MAX_FREAME_LEN = 640;
        string[] videoStrInfos = new string[MAX_FREAME_LEN];

        //Paths
        const string FILE_FORMAT = "{0}_Frame";
        const string SOUND_NAME = "RecordedAudio.wav";
        const string VIDEO_NAME = "ScreenCapture.mp4";
        string cashDir, imgFilePathFormat, firstImgFilePath, soundPath, outputVideoPath;

        //Variables
        int framesCount;
        float startTime, frameInterval, frameTimer, totalTime;

        public bool isREC { get; private set; }
        public bool isProducing { get; private set; }
#if !UNITY_EDITOR
        int initialWidth, initialHeight;
        public bool overrideResolution { get { return width > 0 || height > 0; } }
#endif

        public static readonly int CONST_WIDTH = 600;
        public static readonly int CONST_HEIGTH = 400;

        //References
        Action<string> onOutput, onFinish;
        private RenderTexture rt;

        //PUBLIC INTERFACE
        //------------------------------

        public void Init(Action<string> _onOutput, Action<string> _onFinish)
        {
            //Subscription to FFmpeg callbacks
            FFmpegParser.Handler = this;

            //Paths initialization
            cashDir = Path.Combine(Application.temporaryCachePath, "RecordingCash");
            imgFilePathFormat = Path.Combine(cashDir, FILE_FORMAT);
            firstImgFilePath = String.Format(imgFilePathFormat, "%0d");
            soundPath = Path.Combine(cashDir, SOUND_NAME);
            outputVideoPath = Path.Combine(Application.temporaryCachePath, VIDEO_NAME);

#if !UNITY_EDITOR
            initialWidth = Screen.width;
            initialHeight = Screen.height;
#endif

            //Sound source initialization
            if (recAudioSource == RecAudioSource.Mic)
            {
                soundRecorder = GetComponent<RecMicAudio>();
            }
            else if (recAudioSource == RecAudioSource.System)
            {
                soundRecorder = GetComponent<RecSystemAudio>();
            }
            else if (recAudioSource == RecAudioSource.None)
            {
                soundRecorder = null;
            }

            onOutput = _onOutput;
            onFinish = _onFinish;
            InitCS();
        }

        private void InitCS()
        {
            //找到GPU真正执行的方法在computeShader里的索引
            kernel = shader.FindKernel("SampleTexture");
            pixels = new uint[width * height];
            buffer = new ComputeBuffer(width * height, 4);
            bytes = new byte[pixels.Length * 4];
            texture = new Texture2D(width, height, TextureFormat.RGB565, false);
        }

        void Clear()
        {
            // buffer.Dispose();

            if (Directory.Exists(cashDir))
                Directory.Delete(cashDir, true);
        }

        public void StartREC()
        {
            if (!isREC && !isProducing)
            {
                Clear();
                Directory.CreateDirectory(cashDir);
                Debug.Log("CashDir created: " + cashDir);

                width = (Screen.width / 2) * 2;
                height = (Screen.height / 2) * 2;

                frameBuffer = new Texture2D(width, height, TextureFormat.RGB24, false, true);
                camRect = new Rect(0, 0, width, height);
                startTime = Time.time;
                framesCount = 0;
                frameInterval = 3.0f / FPS;
                frameTimer = frameInterval;

                isREC = true;

                if (recAudioSource != RecAudioSource.None)
                {
                    soundRecorder.StartRecording();
                }
            }
        }

        public void StopREC()
        {
            isREC = false;
            isProducing = true;

            totalTime = Time.time - startTime;
            actualFPS = framesCount / totalTime;

            Debug.Log("Actual FPS: " + actualFPS);

            if (recAudioSource != RecAudioSource.None)
            {
                soundRecorder.StopRecording(soundPath);
            }

            CompressData();

            // CreateVideo();
        }

        private void CompressData()
        {
#if UNITY_IOS || UNITY_ANDROID
            var path = Application.persistentDataPath + "/result.zip";
#elif UNITY_EDITOR
            var path = Application.temporaryCachePath + "/result.zip";
#endif

            System.Threading.Tasks.Task.Factory.StartNew(() => { ZipDir(path); });
            // DirectoryInfo theFolder = new DirectoryInfo(cashDir);
            //
            // //遍历文件
            // foreach (FileInfo file in theFolder.GetFiles())
            // {
            //     var bt = ZipFile.LzmaCompress(File.ReadAllBytes(file.FullName));
            //     File.WriteAllBytes(file.FullName, bt);
            // }
        }

        private void OnGUI()
        {
            if (GUI.Button(new Rect(0, 30, 120, 40), "UnZipAndEncode"))
            {
                // 解压
                UnZipDir();
                // 转换成图片
                ChangeByteToTexture();
            }

            if (GUI.Button(new Rect(0, 70, 120, 40), "CreateVideo"))
            {
                // 创建视频 
                CreateVideo();
            }
        }
        
        private void ChangeByteToTexture()
        {
            DirectoryInfo folder = new DirectoryInfo(Application.temporaryCachePath + "/TempResult");

            if (!Directory.Exists(cashDir))
            {
                Directory.CreateDirectory(cashDir);
            }

            foreach (FileInfo file in folder.GetFiles())
            {
                if (file.FullName.EndsWith(".wav"))
                {
                    File.Copy(file.FullName, file.FullName.Replace("TempResult", "RecordingCash"));
                    continue;
                }

                var rawBt = File.ReadAllBytes(file.FullName);
                var bt = TestByteAttay.gzipDecompress(rawBt);
                var index = 0;
                for (int j = height; j > 0; j--)
                {
                    for (int i = width; i > 0; i--)
                    {
                        var b = bt[index++];
                        var g = bt[index++];
                        var r = bt[index++];
                        var color = new Color(r, g, b) / 255.0f;
                        texture.SetPixel(i, j, color);
                    }
                }

                File.WriteAllBytes(file.FullName.Replace("TempResult", "RecordingCash") + ".jpg",
                    texture.EncodeToJPG());
            }
        }

        //INTERNAL IMPLEMENTATION
        //------------------------------

        // void OnPostRender()
        // {
        //     if (isREC && (frameTimer += Time.deltaTime) > frameInterval)
        //     {
        //         frameTimer -= frameInterval;
        //
        //         if (framesCount >= MAX_FREAME_LEN)
        //         {
        //             framesCount = 0;
        //         }
        //
        //         Texture2D frame = null;
        //         if (frameBuffers1[framesCount] == null)
        //         {
        //             frame = new Texture2D(width, height, TextureFormat.RGB24, false, true);
        //             frame.ReadPixels(camRect, 0, 0);
        //             frameBuffers1[framesCount] = frame;
        //         }
        //         else
        //         {
        //             frameBuffers1[framesCount].ReadPixels(camRect, 0, 0);
        //         }
        //
        //         framesCount++;
        //         // File.WriteAllBytes(NextImgFilePath(), frameBuffer.EncodeToJPG());
        //     }
        // }

        // void OnPostRender()
        // {
        //     if (isREC && (frameTimer += Time.deltaTime) > frameInterval)
        //     {
        //         frameTimer -= frameInterval;
        //
        //         // 将GPU FBO拷贝到CPU的buffer中，这里狠耗
        //         frameBuffer.ReadPixels(camRect, 0, 0);
        //         // 如何更快的获得GPU FBO中的数据 成为了优化的关键
        //
        //         File.WriteAllBytes(NextImgFilePath(), frameBuffer.EncodeToJPG());
        //     }
        // }

        Stopwatch sw = new Stopwatch();
        private void OnRenderImage(RenderTexture src, RenderTexture dest)
        {
            if (isREC && (frameTimer += Time.deltaTime) > frameInterval)
            {
                frameTimer -= frameInterval;
                if (framesCount >= MAX_FREAME_LEN)
                {
                    framesCount = 0;
                }

                var renderTexture = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(src, renderTexture);
                CommitFrame(renderTexture);
                framesCount++;
                RenderTexture.ReleaseTemporary(renderTexture);
            }

            Graphics.Blit(src, dest);
        }

        private byte[] ReadTexture(string path)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            FileStream fileStream = new FileStream(path, FileMode.Open, System.IO.FileAccess.Read);

            fileStream.Seek(0, SeekOrigin.Begin);

            byte[] buffer = new byte[fileStream.Length]; //创建文件长度的buffer   
            fileStream.Read(buffer, 0, (int) fileStream.Length);

            fileStream.Close();

            fileStream.Dispose();

            fileStream = null;

            return buffer;
        }

        public void RecordMovie()
        {
            var videoAttr = new VideoTrackAttributes
            {
                frameRate = new MediaRational(50),
                width = 320,
                height = 200,
                includeAlpha = false
            };

            var audioAttr = new AudioTrackAttributes
            {
                sampleRate = new MediaRational(48000),
                channelCount = 2,
                language = "fr"
            };

            int sampleFramesPerVideoFrame = audioAttr.channelCount *
                audioAttr.sampleRate.numerator / videoAttr.frameRate.numerator;

            var encodedFilePath = Path.Combine(Path.GetTempPath(), "my_movie.mp4");

            Texture2D tex = new Texture2D((int) videoAttr.width, (int) videoAttr.height, TextureFormat.RGBA32, false);

            using (var encoder = new MediaEncoder(encodedFilePath, videoAttr, audioAttr))
            using (var audioBuffer = new NativeArray<float>(sampleFramesPerVideoFrame, Allocator.Temp))
            {
                for (int i = 0; i < 100; ++i)
                {
                    // Fill 'tex' with the video content to be encoded into the file
                    encoder.AddFrame(tex);
                    // Fill 'audioBuffer' with the audio content to be encoded 
                    encoder.AddSamples(audioBuffer);
                }
            }
        }

        private void CommitFrame(RenderTexture renderTexture)
        {
            //把我们准备好的数据塞给Buffer里
            buffer.SetData(pixels);
            //把我们之前准备好的buffer数据塞给computeShader，这样就建立了一个gpu到cpu的数据连接，gpu在计算时
            //会使用当前这个buffer里的数据。
            //注意：下面方法中的第二个参数 必须与 shader 里对应的那个 buffer 的变量名一模一样
            // shader.SetBuffer(kernel, "ParticleBuffer", buffer);
            shader.SetBuffer(kernel, "result", buffer);
            // shader.SetTexture(kernel, "writer", texture);
            shader.SetTexture(kernel, "reader", renderTexture);
            // shader.SetInt("width", width);
            // shader.SetInt("height", height);
            shader.Dispatch(kernel, width / 32, height / 32, 1);

            buffer.GetData(pixels);
            //
            System.Threading.Tasks.Task.Factory.StartNew(() =>
            {
                // Stopwatch sw = new Stopwatch();
                // sw.Start();
            
                List<byte> arrBt = new List<byte>(bytes.Length * 3 / 4);
                for (int i = 0; i < pixels.Length; i++)
                {
                    var pixel = pixels[i];
                    var r = pixel >> 16;
                    var g = (pixel >> 8) - (r << 8);
                    var b = pixel - ((pixel >> 8) << 8);
                    arrBt.Add((byte) b);
                    arrBt.Add((byte) g);
                    arrBt.Add((byte) r);
                }
                // sw.Stop();
                // Debug.Log(sw.ElapsedMilliseconds);
            
                File.WriteAllBytes(String.Format(imgFilePathFormat, framesCount),
                    TestByteAttay.gzipCompress(arrBt.ToArray()));
            });
        }

        private void WritePic()
        {
            var index = 0;
            var inteval = width * height / 240000;
            // Debug.Log(inteval);
            for (int j = CONST_HEIGTH; j > 0; j--)
            {
                for (int i = CONST_WIDTH; i > 0; i--)
                {
                    var pixel = pixels[index];
                    var r = pixel >> 16;
                    var g = (pixel >> 8) - (r << 8);
                    var b = pixel - ((pixel >> 8) << 8);
                    var color = new Color(r, g, b) / 255.0f;
                    texture.SetPixel(i, j, color);

                    index += inteval;
                }
            }

            File.WriteAllBytes(String.Format(imgFilePathFormat, framesCount) + ".jpg", texture.EncodeToJPG());
        }

        private string NextImgFilePath()
        {
            return String.Format(imgFilePathFormat, framesCount++);
        }

        private void ZipDir(string path)
        {
            using (var zip = new ZipFile())
            {
                // 设置压缩密码
                zip.Password = "HLMJ123456";
                zip.AddDirectory(cashDir);
                zip.Save(path);
            }
        }

        private void UnZipDir()
        {
            using (ZipFile zip = new ZipFile(Application.temporaryCachePath + "/result.zip"))
            {
                // 设置解压密码
                zip.Password = "HLMJ123456";
                zip.ExtractAll(Application.temporaryCachePath + "/TempResult");
            }
        }

        void CreateVideo()
        {
            StringBuilder command = new StringBuilder();

            //Input Image sequence params
            command.
                // Append("-y -framerate ").
                // Append(actualFPS.ToString()).
                Append(" -f image2 -i ").Append(AddQuotation(firstImgFilePath)); // //Input Audio params
            if (recAudioSource != RecAudioSource.None)
            {
                command.Append(" -i ").Append(AddQuotation(soundPath)).Append(" -ss 0 -t ").Append(totalTime);
            }

            //Output Video params
            command.Append(" -vcodec libx264 -crf 25 -pix_fmt yuv420p ")
                .Append(AddQuotation(
                    outputVideoPath));
            // command.Append(" -vcodec libx264 -crf 25 -pix_fmt yuv420p ").Append(AddQuotation(outputVideoPath));

            FFmpegCommands.DirectInput(command.ToString());
        }

        private void WriteTexture()
        {
            var startIndex = framesCount % MAX_FREAME_LEN;
            string[] strs = new string[MAX_FREAME_LEN];
            for (int i = startIndex; i < MAX_FREAME_LEN; i++)
            {
                strs[i - startIndex] = videoStrInfos[i];
            }

            for (int i = 0; i < startIndex; i++)
            {
                strs[i - startIndex + MAX_FREAME_LEN] = videoStrInfos[i];
            }

            var strPath = Path.Combine(cashDir, "recordVideo.txt");
            File.WriteAllLines(strPath, strs);
        }

        string AddQuotation(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new Exception("Empty path.");
            }
#if UNITY_EDITOR || UNITY_STANDALONE
            const char DOUBLE_QUOTATION = '\"';
            if (path[0] != DOUBLE_QUOTATION)
            {
                return DOUBLE_QUOTATION + path + DOUBLE_QUOTATION;
            }
#endif
            return path;
        }

        //FFmpeg processing callbacks
        //------------------------------

        //Begining of video processing
        public void OnStart()
        {
            onOutput("VideoProducing Started\n");
        }

        //You can make custom progress bar here (parse msg)
        public void OnProgress(string msg)
        {
            onOutput(msg);
        }

        //Notify user about failure here
        public void OnFailure(string msg)
        {
            onOutput(msg);
        }

        //Notify user about success here
        public void OnSuccess(string msg)
        {
            onOutput(msg);
        }

        //Last callback - do whatever you need next
        public void OnFinish()
        {
            onFinish(outputVideoPath);
            Clear();
            isProducing = false;
        }
    }
}