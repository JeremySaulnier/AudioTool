using UnityEditor;
using UnityEngine;
using System.IO;

public class AudioSampler : EditorWindow
{
    private const int m_Gap = 10;

    private GameObject m_Playback;
    private AudioSource m_AudioSource;
    private AudioClip m_AudioClip;
    private AudioClip m_LastClip;

    private float[] m_Samples;
    private int m_SampleCount;

    private float m_Start = 0f;
    private float m_End = 100f;
    private float m_FadeInAmount = 0f;
    private float m_FadeOutAmount = 0f;

    private string m_Name;
    private float m_Volume = 1f;

    private bool m_IsDraggingStart = false;
    private bool m_IsDraggingEnd = false;

    public AnimationCurve m_FadeInCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    public AnimationCurve m_FadeOutCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

    [MenuItem("Audio/Sampler")]
    public static void ShowWindow()
    {
        GetWindow<AudioSampler>("Sampler");
    }

    private void OnEnable()
    {
        m_Playback = new GameObject("Sampler");
        m_AudioSource = m_Playback.AddComponent<AudioSource>();
    }

    private void OnDisable()
    {
        DestroyImmediate(m_Playback);
    }

    private void OnGUI()
    {
        Space(m_Gap);
        HBegin();
        {
            GUILayout.Label("Audio Clip", EditorStyles.boldLabel);
            m_AudioClip = (AudioClip)EditorGUILayout.ObjectField(m_AudioClip, typeof(AudioClip), true);
            if (m_AudioClip != m_LastClip)
                OnClipSelect();

            m_Name = EditorGUILayout.TextField("Name", m_Name);
        }
        HEnd();

        if (m_AudioClip)
        {
            Rect previewRect = GUILayoutUtility.GetRect(position.width, 150f);
            Handles.DrawSolidRectangleWithOutline(previewRect, Color.gray, Color.gray);
            Space(m_Gap);

            GenerateWaveform(previewRect);
            SetRange(previewRect);
            SetFade(previewRect);

            HBegin();
            {
                if (GUILayout.Button("Play"))
                    Play();

                if (GUILayout.Button("Stop"))
                    Stop();
            }
            HEnd();

            HBegin();
            {
                VBegin();
                {
                    EditorGUILayout.LabelField("Fade In");
                    m_FadeInAmount = EditorGUILayout.Slider(m_FadeInAmount, 0f, 100f);
                    m_FadeInCurve = EditorGUILayout.CurveField(m_FadeInCurve);
                }
                VEnd();
                VBegin();
                {
                    EditorGUILayout.LabelField("Fade Out");
                    m_FadeOutAmount = EditorGUILayout.Slider(m_FadeOutAmount, 0f, 100f);
                    m_FadeOutCurve = EditorGUILayout.CurveField(m_FadeOutCurve);
                }
                VEnd();
                VBegin();
                {
                    EditorGUILayout.LabelField("Volume");
                    m_Volume = EditorGUILayout.Slider(m_Volume, 0.1f, 1f);
                    if (GUILayout.Button("Normalize"))
                        Normalize();
                }
                VEnd();
            }
            HEnd();

            if (GUILayout.Button("Export"))
                Export();
        }

        if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Space)
        {
            if (m_AudioSource.isPlaying)
                Stop();
            else
                Play();
        }
    }

    private void OnClipSelect()
    {
        m_LastClip = m_AudioClip;
        m_Name = m_AudioClip.name + "_new";
        m_Start = 0f;
        m_End = 100f;
    }

    private void Play()
    {
        m_AudioSource.volume = m_Volume;
        m_AudioSource.clip = GetUpdatedClip();
        m_AudioSource.Play();
    }

    private void Stop()
    {
        m_AudioSource.Stop();
    }

    private void Normalize()
    {
        float highestPeak = 0f;
        for (int i = 0; i < m_SampleCount; i++)
            highestPeak = Mathf.Max(highestPeak, Mathf.Abs(m_Samples[i]));

        float volumeMultiplier = 1f / highestPeak;
        for (int i = 0; i < m_SampleCount; i++)
            m_Samples[i] *= volumeMultiplier;

        m_Volume = 1f;
        m_AudioClip.SetData(m_Samples, 0);
    }

    private void SetRange(Rect rect)
    {
        Event currentEvent = Event.current;
        if (currentEvent.type == EventType.MouseDown && rect.Contains(currentEvent.mousePosition))
        {
            float clickPosition = Mathf.InverseLerp(rect.xMin, rect.xMax, currentEvent.mousePosition.x);
            float distanceToStart = Mathf.Abs(clickPosition - m_Start / 100f);
            float distanceToEnd = Mathf.Abs(clickPosition - m_End / 100f);

            if (distanceToStart < distanceToEnd)
                m_IsDraggingStart = true;
            else
                m_IsDraggingEnd = true;
        }
        else if (currentEvent.type == EventType.MouseDrag)
        {
            float dragPosition = Mathf.InverseLerp(rect.xMin, rect.xMax, currentEvent.mousePosition.x);

            if (m_IsDraggingStart)
                m_Start = Mathf.Clamp(dragPosition * 100f, 0f, m_End);
            else if (m_IsDraggingEnd)
                m_End = Mathf.Clamp(dragPosition * 100f, m_Start, 100f);

            Repaint();
        }
        else if (currentEvent.type == EventType.MouseUp)
        {
            m_IsDraggingStart = false;
            m_IsDraggingEnd = false;
        }

        float start = rect.x + (m_Start * 0.01f) * rect.width;
        float end = rect.x + (m_End * 0.01f) * rect.width;

        Handles.color = Color.red;
        Handles.DrawLine(new Vector3(start, rect.yMin), new Vector3(start, rect.yMax));
        Handles.DrawLine(new Vector3(end, rect.yMin), new Vector3(end, rect.yMax));
    }

    private void GenerateWaveform(Rect rect)
    {
        m_Samples = new float[m_AudioClip.samples * m_AudioClip.channels];
        m_SampleCount = m_AudioClip.samples * m_AudioClip.channels;
        m_AudioClip.GetData(m_Samples, 0);

        Vector3 lastPoint = new Vector3(rect.x, rect.y + rect.height, 0f);
        for (int i = 0; i < m_Samples.Length; i += Mathf.FloorToInt(m_Samples.Length / rect.width))
        {
            int sampleIndex = Mathf.Clamp(i, 0, m_Samples.Length - 1);
            float sampleValue = Mathf.Abs(m_Samples[sampleIndex]) * m_Volume;
            float amplitude = Mathf.InverseLerp(0f, 1f, sampleValue);

            float x = rect.x + i * rect.width / m_Samples.Length;
            float y = rect.y + (1 - amplitude) * rect.height;
            Vector3 currentPoint = new Vector3(x, y);

            Handles.color = Color.yellow;
            Handles.DrawAAPolyLine(lastPoint, currentPoint);

            lastPoint = currentPoint;
        }
    }

    private void SetFade(Rect rect)
    {
        int sampleStart = Mathf.FloorToInt(m_AudioClip.samples * m_Start * 0.01f);
        int sampleEnd = Mathf.FloorToInt(m_AudioClip.samples * m_End * 0.01f);

        float fadeInStart = rect.x + rect.width * sampleStart / (float)m_AudioClip.samples;
        float fadeInEnd = rect.x + rect.width * sampleEnd / (float)m_AudioClip.samples;
        Rect fadeInRect = new Rect(fadeInStart, rect.y, fadeInEnd - fadeInStart, rect.height);
        DrawFade(fadeInRect, m_FadeInCurve, m_FadeInAmount, false);

        float fadeOutStart = rect.x + rect.width * sampleStart / (float)m_AudioClip.samples;
        float fadeOutEnd = rect.x + rect.width * sampleEnd / (float)m_AudioClip.samples;
        Rect fadeOutRect = new Rect(fadeOutStart, rect.y, fadeOutEnd - fadeOutStart, rect.height);
        DrawFade(fadeOutRect, m_FadeOutCurve, m_FadeOutAmount, true);
    }

    private void DrawFade(Rect rect, AnimationCurve curve, float value, bool reversed)
    {
        Handles.color = Color.blue;
        Vector3 lastPoint = Vector3.zero;

        float start = reversed ? rect.x + rect.width * (1f - value * 0.01f) : rect.x;
        float end = reversed ? rect.x + rect.width : rect.x + rect.width * value * 0.01f;

        for (float i = start; i <= end; i++)
        {
            float time = Mathf.InverseLerp(start, end, i);
            float eval = curve.Evaluate(time);
            float y = rect.y + (1 - eval) * rect.height;

            Vector3 currentPoint = new Vector3(i, y);

            if (i > start)
                Handles.DrawAAPolyLine(lastPoint, currentPoint);

            lastPoint = currentPoint;
        }
    }

    private AudioClip GetUpdatedClip()
    {
        int start = Mathf.FloorToInt(m_AudioClip.samples * m_Start * 0.01f);
        int end = Mathf.FloorToInt(m_AudioClip.samples * m_End * 0.01f);
        int lenght = end - start;
        int count = lenght > 0 ? lenght : 1;

        m_Samples = new float[count * m_AudioClip.channels];
        m_AudioClip.GetData(m_Samples, start);

        int fadeInSamples = Mathf.FloorToInt(count * m_FadeInAmount * 0.01f);
        int fadeOutSamples = Mathf.FloorToInt(count * m_FadeOutAmount * 0.01f);

        for (int i = 0; i < count; i++)
        {
            float fadeInMultiplier = (i < fadeInSamples) ? m_FadeInCurve.Evaluate(i / (float)fadeInSamples) : 1f;
            float fadeOutMultiplier = (i >= count - fadeOutSamples) ? 1f - m_FadeOutCurve.Evaluate((count - i) / (float)fadeOutSamples) : 1f;
            int sampleIndex = i * m_AudioClip.channels;

            for (int channel = 0; channel < m_AudioClip.channels; channel++)
                m_Samples[sampleIndex + channel] *= m_Volume * fadeInMultiplier * fadeOutMultiplier;
        }

        AudioClip newClip = AudioClip.Create(m_AudioClip.name, count, m_AudioClip.channels, m_AudioClip.frequency, false);
        newClip.SetData(m_Samples, 0);

        return newClip;
    }

    private void Export()
    {
        AudioClip newClip = GetUpdatedClip();

        string folderPath = Path.Combine(Application.dataPath, "Audio");
        string filePath = Path.Combine(folderPath, m_Name + ".wav");
        Directory.CreateDirectory(folderPath);

        using (FileStream stream = new FileStream(filePath, FileMode.Create))
        WriteFile(new BinaryWriter(stream), newClip.frequency, newClip.channels, m_Samples.Length);

        AssetDatabase.Refresh();
    }

    private void WriteFile(BinaryWriter writer, int sampleRate, int channelCount, int sampleCount)
    {
        writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + sampleCount * sizeof(short));
        writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)channelCount);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channelCount * sizeof(short));
        writer.Write((short)(channelCount * sizeof(short)));
        writer.Write((short)16);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        writer.Write(sampleCount * sizeof(short));

        foreach (float sample in m_Samples)
        {
            short sampleValue = (short)Mathf.Clamp(sample * m_Volume * short.MaxValue, short.MinValue, short.MaxValue);
            writer.Write(sampleValue);
        }
    }

    private void HBegin()
    {
        GUILayout.BeginHorizontal();
    }

    private void HEnd()
    {
        GUILayout.EndHorizontal();
        GUILayout.Space(m_Gap);
    }

    private void VBegin()
    {
        GUILayout.BeginVertical();
    }

    private void VEnd()
    {
        GUILayout.EndVertical();
        GUILayout.Space(m_Gap);
    }

    private void Space(float space)
    {
        GUILayout.Space(space);
    }
}