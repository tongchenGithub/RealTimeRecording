using UnityEngine;

public class FPSDisplay : MonoBehaviour
{
    private float deltaTime;
    private Rect rect;
    private GUIStyle style;

    private void Start()
    {
        int w = Screen.width, h = Screen.height;
        style = new GUIStyle();
        rect = new Rect(0, 0, w, h * 2 / 100);
        style.alignment = TextAnchor.UpperLeft;
        style.fontSize = h * 2 / 100;
        style.normal.textColor = new Color(1.0f, 1.0f, 1.0f, 1.0f);
    }

    void Update()
    {
        deltaTime += (Time.unscaledDeltaTime - deltaTime) * 0.1f;
    }

    void OnGUI()
    {
        float msec = deltaTime * 1000.0f;
        float fps = 1.0f / deltaTime;
        GUI.Label(rect, string.Format("{0:0.0} ms ({1:0.} fps)", msec, fps), style);
    }
}