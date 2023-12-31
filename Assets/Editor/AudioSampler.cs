using UnityEditor;
using UnityEngine;
using System.IO;

public class AudioSampler : EditorWindow
{
    private const float m_GapSize = 2f;
    private const int m_MiniLabelSize = 40;
    private const int m_SmallLabelSize = 60;
    private const int m_SmallFixedRatio = 30;
    private readonly Color m_Gray = new Color(0.12f, 0.12f, 0.12f);
    private readonly Color m_Orange = new Color(1.0f, 0.6f, 0.0f);
    private static Vector2 m_DefaultWindowSize = new Vector2(300, 395);
    private Rect m_Visualiser;
    private Rect m_Section;

    private AudioSource m_AudioSource;
    private AudioClip m_ReferenceClip;
    private AudioClip m_CopyClip;
    private AudioClip m_LastClip;

    private float[] m_Samples;
    private int m_SampleCount;
    private int m_StartSample;
    private int m_EndSample;

    private float m_StartPosition;
    private float m_EndPosition;
    private float m_StartRatio = 0;
    private float m_EndRatio = 100;
    
    private float m_Volume = 1f;
    private float m_Pitch = 1f;

    private enum EFadeType { Convex, Smooth, Concave }
    private EFadeType m_FadeInType = EFadeType.Smooth;
    private EFadeType m_FadeOutType = EFadeType.Smooth;
    private readonly AnimationCurve m_FadeInSmooth = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    private readonly AnimationCurve m_FadeOutSmooth = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
    private readonly AnimationCurve m_FadeInConvex = new AnimationCurve(new Keyframe(0f, 0f, 0f, 3f), new Keyframe(1f, 1f, 0f, 0f));
    private readonly AnimationCurve m_FadeOutConvex = new AnimationCurve(new Keyframe(0f, 1f, 0f, -3f), new Keyframe(1f, 0f, 0f, 0f));
    private readonly AnimationCurve m_FadeInConcave = new AnimationCurve(new Keyframe(0f, 0f, 0f, 0f), new Keyframe(1f, 1f, 3f, 0f));
    private readonly AnimationCurve m_FadeOutConcave = new AnimationCurve(new Keyframe(0f, 1f, 0f, 0f), new Keyframe(1f, 0f, -3f, 0f));
    private AnimationCurve m_FadeInCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    private AnimationCurve m_FadeOutCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
    private float m_FadeInAmount = 0f;
    private float m_FadeOutAmount = 0f;

    private enum EFilterType { None, LowPass, HighPass }
    private EFilterType m_FilterType = EFilterType.LowPass;
    private int m_FilterAmount = 0;
    float m_FilterIn1 = 0f;
    float m_FilterIn2 = 0f;
    float m_FilterOut1 = 0f;
    float m_FilterOut2 = 0f;

    private int m_Index;
    private string m_Name;
    private string m_ExportPath;
    private string[] m_FolderPaths;
    private int m_SelectedPath = 0;
    private bool m_ChangedName = false;
    private bool m_ChangedSettings = true;

    [MenuItem("Audio/Sampler")]
    public static void ShowWindow()
    {
        var window = GetWindow<AudioSampler>("Sampler");
        window.position = new Rect(200f, 200f, m_DefaultWindowSize.x, m_DefaultWindowSize.y);
    }

    private void OnEnable()
    {
        minSize = m_DefaultWindowSize;
        LoadAudioClip();

        m_FolderPaths = Directory.GetDirectories("Assets");
        for (int i = 0; i < m_FolderPaths.Length; i++)
            m_FolderPaths[i] = Path.GetFileName(m_FolderPaths[i]);

        m_AudioSource = EditorUtility.CreateGameObjectWithHideFlags("Audio", HideFlags.HideAndDontSave, typeof(AudioSource)).GetComponent<AudioSource>();
    }

    private void OnDisable()
    {
        Stop();
        DestroyImmediate(m_AudioSource);
    }

    private void OnGUI()
    {
        m_Visualiser = new Rect(8f, 34f, position.width - 16f, 120f + (position.height - m_DefaultWindowSize.y));

        Space(4f);
        HBegin(true);
        {
            EditorGUILayout.LabelField("Clip", GUILayout.Width(m_MiniLabelSize));
            CheckChange();
            {
                m_ReferenceClip = (AudioClip)EditorGUILayout.ObjectField(m_ReferenceClip, typeof(AudioClip), true);

                if (m_ReferenceClip != m_LastClip)
                {
                    SetToDefault();
                    m_Index = 0;
                }
            } 

            if (GUILayout.Button("Reset", GUILayout.Width(WidthFromRatio(m_SmallFixedRatio))))
                SetToDefault();
        }
        HEnd();

        if (m_CopyClip)
        {
            Space(m_Visualiser.height);
            Handles.DrawSolidRectangleWithOutline(m_Visualiser, m_Gray, m_Gray);

            UpdateData();
            UpdateWaveform();
            UpdateRange();
            UpdateFadeIn();
            UpdateFadeOut();

            EditorGUILayout.MinMaxSlider(ref m_StartRatio, ref m_EndRatio, 0f, 100f);

            HBegin(true);
            {
                if (GUILayout.Button("Play"))
                    Play();

                if (GUILayout.Button("Stop"))
                    Stop();
            }
            HEndWithoutCheck();
            HBegin(true);
            {
                VBegin();
                {
                    HBegin(false);
                    {
                        EditorGUILayout.LabelField("Volume", GUILayout.Width(m_SmallLabelSize));
                        m_Volume = EditorGUILayout.Slider(m_Volume, 0f, 1f);

                        if (GUILayout.Button("Normalize", GUILayout.Width(WidthFromRatio(m_SmallFixedRatio))))
                            Normalize();
                    }
                    HEnd();
                    HBegin(false);
                    {
                        EditorGUILayout.LabelField("Pitch", GUILayout.Width(m_SmallLabelSize));
                        m_Pitch = EditorGUILayout.Slider(m_Pitch, 0.5f, 2f);

                        if (GUILayout.Button("Reverse", GUILayout.Width(WidthFromRatio(m_SmallFixedRatio))))
                            Reverse();
                    }
                    HEnd();
                }
                VEnd();
            }
            HEnd();
            HBegin(true);
            {
                VBegin();
                {
                    HBegin(false);
                    {
                        EditorGUILayout.LabelField("Attack", GUILayout.Width(m_SmallLabelSize));
                        m_FadeInAmount = EditorGUILayout.Slider(m_FadeInAmount, 0f, 100f);
                        CheckChange();
                        {
                            m_FadeInType = (EFadeType)EditorGUILayout.EnumPopup(m_FadeInType, GUILayout.Width(WidthFromRatio(m_SmallFixedRatio)));
                        }
                        Unfocus();
                    }
                    HEnd();
                    HBegin(false);
                    {
                        EditorGUILayout.LabelField("Release", GUILayout.Width(m_SmallLabelSize));
                        m_FadeOutAmount = EditorGUILayout.Slider(m_FadeOutAmount, 0f, 100f);
                        CheckChange();
                        {
                            m_FadeOutType = (EFadeType)EditorGUILayout.EnumPopup(m_FadeOutType, GUILayout.Width(WidthFromRatio(m_SmallFixedRatio)));
                        }
                        Unfocus();
                    }
                    HEnd();
                }
                VEnd();
            }
            HEnd();
            HBegin(true);
            {
                EditorGUILayout.LabelField("Filter", GUILayout.Width(m_SmallLabelSize));
                m_FilterAmount = EditorGUILayout.IntSlider(m_FilterAmount, 0, 100);
                CheckChange();
                {
                    m_FilterType = (EFilterType)EditorGUILayout.EnumPopup(m_FilterType, GUILayout.Width(WidthFromRatio(m_SmallFixedRatio)));
                }
                Unfocus();
            }
            HEnd();
            HBegin(true);
            {
                VBegin();
                {
                    HBegin(false);
                    {
                        CheckChange();
                        {
                            EditorGUILayout.LabelField("Name", GUILayout.Width(m_SmallLabelSize));
                            m_Name = EditorGUILayout.TextField(m_Name);
                            if (EndCheck())
                            {
                                if (string.IsNullOrWhiteSpace(m_Name))
                                    m_Name = "Sample";
                                m_ChangedName = true;
                                m_Index = 0;
                            }
                        }
                        CheckChange();
                        {
                            m_SelectedPath = EditorGUILayout.Popup(m_SelectedPath, m_FolderPaths, GUILayout.Width(WidthFromRatio(m_SmallFixedRatio)));
                            m_ExportPath = m_FolderPaths[m_SelectedPath];
                        }
                        Unfocus();
                    }
                    HEnd();
                    HBegin(false);
                    {
                        if (GUILayout.Button("Export"))
                            Export();
                    }
                    HEnd();
                }
                VEnd();
            }
            HEnd();

            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Space)
                Play();
        }
        else
        {
            // TODO : Figure out why and add to message.
            EditorGUILayout.HelpBox("Can't handle this audio clip.", MessageType.Error);
        }
    }

    private void LoadAudioClip()
    {
        string[] clips = new string[0];
        string audioFolder = "Assets/Audio";

        if (AssetDatabase.IsValidFolder(audioFolder))
            clips = AssetDatabase.FindAssets("t:AudioClip", new[] { audioFolder });

        if (m_LastClip)
            m_ReferenceClip = m_LastClip;
        else if (clips.Length > 0)
            m_ReferenceClip = (AudioClip)AssetDatabase.LoadAssetAtPath<Object>(AssetDatabase.GUIDToAssetPath(clips[0]));

        SetToDefault();
    }

    private void SetToDefault()
    {
        if (m_ReferenceClip)
        {
            m_LastClip = m_ReferenceClip;
            m_CopyClip = GetCopiedClip(m_ReferenceClip);
            m_Name = m_ReferenceClip.name + "_new";
        }

        m_Volume = 1f;
        m_Pitch = 1f;
        m_StartRatio = 0f;
        m_EndRatio = 100f;
        m_FadeInAmount = 0f;
        m_FadeOutAmount = 0f;
        m_FilterAmount = 0;
    }

    private void UpdateData()
    {
        m_Samples = new float[m_CopyClip.samples * m_CopyClip.channels];
        m_StartSample = (int)(m_CopyClip.samples * m_StartRatio * 0.01f);
        m_EndSample = (int)(m_CopyClip.samples * m_EndRatio * 0.01f);
        m_SampleCount = m_Samples.Length;
        m_CopyClip.GetData(m_Samples, 0);
    }

    private void UpdateWaveform()
    {
        int count = 5000;
        float centerY = m_Visualiser.y + m_Visualiser.height / 2;
        for (int i = 0; i < count; i++)
        {
            int index = (int)(i * (float)m_SampleCount / count);
            float height = m_Samples[index] * m_Volume * m_Visualiser.height / 2;
            float x = m_Visualiser.x + i * m_Visualiser.width / count;

            Vector3 start = new Vector3(x, centerY);
            Vector3 end = new Vector3(x, centerY - height);

            Handles.color = m_Orange;
            Handles.DrawLine(start, end);
        }
    }

    private void Play()
    {
        if (m_AudioSource)
        {
            if (m_ChangedSettings)
                m_AudioSource.clip = GetUpdatedClip();

            m_AudioSource.volume = m_Volume;
            m_AudioSource.Play();
        }
    }

    private void Stop()
    {
        if (m_AudioSource && m_AudioSource.isPlaying)
            m_AudioSource.Stop();
    }

    private void Normalize()
    {
        float highestPeak = 0f;
        for (int i = 0; i < m_SampleCount; i++)
            highestPeak = Mathf.Max(highestPeak, Mathf.Abs(m_Samples[i]));

        for (int i = 0; i < m_SampleCount; i++)
            m_Samples[i] *= (1 / highestPeak - 0.01f);

        m_Volume = 1f;
        m_CopyClip.SetData(m_Samples, 0);
    }

    private void Reverse()
    {
        int index = 0;
        float[] samples = new float[m_SampleCount];
        for (int i = m_SampleCount - 1; i > 0; i--)
        {
            samples[index] = m_Samples[i];
            index++;
        }

        m_CopyClip.SetData(samples, 0);
    }

    private void UpdateRange()
    {
        Event current = Event.current;
        float dragPosition = Mathf.InverseLerp(m_Visualiser.xMin, m_Visualiser.xMax, current.mousePosition.x);

        Color startColor = Color.white;
        Color endColor = Color.white;

        if ((current.type == EventType.MouseDrag || current.type == EventType.MouseDown) && m_Visualiser.Contains(Event.current.mousePosition))
        {
            float distanceToStart = Mathf.Abs(dragPosition - m_StartRatio / 100f);
            float distanceToEnd = Mathf.Abs(dragPosition - m_EndRatio / 100f);

            if (distanceToStart < distanceToEnd)
                m_StartRatio = Mathf.Clamp(dragPosition * 100f, 0f, m_EndRatio);
            else
                m_EndRatio = Mathf.Clamp(dragPosition * 100f, m_StartRatio, 100f);

            m_ChangedSettings = true;
            Repaint();
        }

        if (m_StartRatio <= 0.5f)
        {
            m_StartRatio = 0;
            startColor = m_Gray;
        }

        if (m_EndRatio >= 99.5f)
        {
            m_EndRatio = 100;
            endColor = m_Gray;
        }

        m_StartPosition = m_Visualiser.x + (m_StartRatio * 0.01f) * m_Visualiser.width;
        m_EndPosition = m_Visualiser.x + (m_EndRatio * 0.01f) * m_Visualiser.width;
        m_Section = new Rect(m_StartPosition, m_Visualiser.y, m_EndPosition - m_StartPosition, m_Visualiser.height);

        Handles.color = startColor;
        Handles.DrawLine(new Vector3(m_StartPosition, m_Visualiser.yMin), new Vector3(m_StartPosition, m_Visualiser.yMax));
        Handles.color = endColor;
        Handles.DrawLine(new Vector3(m_EndPosition, m_Visualiser.yMin), new Vector3(m_EndPosition, m_Visualiser.yMax));
    }

    private void UpdateFadeIn()
    {
        Handles.color = Color.white;
        Vector3 last = Vector3.zero;

        float start = m_Section.x;
        float end = m_Section.x + m_Section.width * m_FadeInAmount * 0.01f;

        switch (m_FadeInType)
        {
            case EFadeType.Convex:
                m_FadeInCurve = m_FadeInConvex;
                break;
            case EFadeType.Smooth:
                m_FadeInCurve = m_FadeInSmooth;
                break;
            case EFadeType.Concave:
                m_FadeInCurve = m_FadeInConcave;
                break;
        }

        for (float x = start; x <= end; x++)
        {
            float time = Mathf.InverseLerp(start, end, x);
            float eval = m_FadeInCurve.Evaluate(Mathf.Clamp01(time));
            float y = Mathf.Clamp(m_Section.y + (1 - eval) * m_Section.height, m_Section.y, m_Section.y + m_Section.height);

            Vector3 current = new Vector3(x, y);
            if (x > start)
                Handles.DrawAAPolyLine(last, current);
            last = current;
        }
    }

    private void UpdateFadeOut()
    {
        Handles.color = Color.white;
        Vector3 last = Vector3.zero;

        float start = m_Section.x + m_Section.width * (1f - m_FadeOutAmount * 0.01f);
        float end = m_Section.x + m_Section.width;

        switch (m_FadeOutType)
        {
            case EFadeType.Convex:
                m_FadeOutCurve = m_FadeOutConvex;
                break;
            case EFadeType.Smooth:
                m_FadeOutCurve = m_FadeOutSmooth;
                break;
            case EFadeType.Concave:
                m_FadeOutCurve = m_FadeOutConcave;
                break;
        }

        for (float x = start; x <= end; x++)
        {
            AnimationCurve dumbFix = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
            if (m_FadeOutCurve == m_FadeOutConcave)
                dumbFix = m_FadeOutConvex;
            else if (m_FadeOutCurve == m_FadeOutConvex)
                dumbFix = m_FadeOutConcave;

            float time = Mathf.InverseLerp(start, end, x);
            float eval = dumbFix.Evaluate(Mathf.Clamp01(time));
            float y = Mathf.Clamp(m_Section.y + (1 - eval) * m_Section.height, m_Section.y, m_Section.y + m_Section.height);

            Vector3 current = new Vector3(x, y);
            if (x > start)
                Handles.DrawAAPolyLine(last, current);
            last = current;
        }
    }
    
    private float Filter(float sample, float a0, float a1, float a2, float b0, float b1, float b2)
    {
        float filtered = (b0 * sample + b1 * m_FilterIn1 + b2 * m_FilterIn2 - a1 * m_FilterOut1 - a2 * m_FilterOut2) / a0;

        m_FilterIn2 = m_FilterIn1;
        m_FilterIn1 = sample;
        m_FilterOut2 = m_FilterOut1;
        m_FilterOut1 = filtered;

        return filtered;
    }

    private AudioClip GetUpdatedClip()
    {
        int lenght = m_EndSample - m_StartSample;
        int sampleCount = lenght > 0 ? lenght : 1;
        int channelCount = m_CopyClip.channels;

        float fadeInSamples = sampleCount * m_FadeInAmount * 0.01f;
        float fadeOutSamples = sampleCount * m_FadeOutAmount * 0.01f;

        bool isLowPass = m_FilterType == EFilterType.LowPass;
        float amount = isLowPass ? m_FilterAmount : 100f - m_FilterAmount;
        float cutoff = Mathf.Lerp(20000f, 20f, Mathf.Pow(amount / 100f, 0.2f));

        float frequency = 2 * Mathf.PI * cutoff / m_CopyClip.frequency;
        float sin = Mathf.Sin(frequency);
        float cos = Mathf.Cos(frequency);

        float a0 = 1 + (sin / 2);
        float a1 = -2 * cos;
        float a2 = 1 - (sin / 2);

        float b0 = isLowPass ? (1 - cos) / 2 : (1 + cos) / 2;
        float b1 = isLowPass ? 1 - cos : -(1 + cos);
        float b2 = isLowPass ? (1 - cos) / 2 : (1 + cos) / 2;

        m_Samples = new float[sampleCount * channelCount];
        m_CopyClip.GetData(m_Samples, m_StartSample);

        for (int channel = 0; channel < channelCount; channel++)
        {
            for (int sample = 0; sample < sampleCount; sample++)
            {
                int index = sample * channelCount + channel;
                float newSample = m_Samples[index];

                float fadeIn = sample < fadeInSamples ? m_FadeInCurve.Evaluate(sample / fadeInSamples) : 1f;
                float fadeOut = sample >= sampleCount - fadeOutSamples ? 1f - m_FadeOutCurve.Evaluate((sampleCount - sample) / fadeOutSamples) : 1f;
                
                newSample *= m_Volume * fadeIn * fadeOut;

                if (m_FilterType != EFilterType.None)
                    newSample = Filter(newSample, a0, a1, a2, b0, b1, b2) * m_Volume * fadeIn * fadeOut;

                m_Samples[index] = newSample;
            }
        }

        AudioClip clip = AudioClip.Create(m_CopyClip.name, sampleCount, channelCount, (int)(m_CopyClip.frequency * m_Pitch), false);
        clip.SetData(m_Samples, 0);
        m_ChangedSettings = false;

        return clip;
    }

    private AudioClip GetCopiedClip(AudioClip reference)
    {
        float[] data = new float[reference.samples * reference.channels];
        reference.GetData(data, 0);

        AudioClip newClip = AudioClip.Create(reference.name, reference.samples, reference.channels, reference.frequency, false);
        newClip.SetData(data, 0);

        return newClip;
    }

    private void Export()
    {
        m_Index++;

        string fileName = m_Name + "_" + m_Index + ".wav";
        if (m_ReferenceClip != m_LastClip || m_ChangedName)
            fileName = m_Name + ".wav";

        string folderPath = Path.Combine(Application.dataPath, m_ExportPath);
        string filePath = Path.Combine(folderPath, fileName);
        Directory.CreateDirectory(folderPath);

        AudioClip newClip = GetUpdatedClip();

        using (FileStream stream = new FileStream(filePath, FileMode.Create))
        WriteFile(new BinaryWriter(stream), newClip.frequency, newClip.channels, m_SampleCount);

        m_ChangedName = false;
        AssetDatabase.Refresh();
    }

    private void WriteFile(BinaryWriter writer, int sampleRate, int channelCount, int sampleCount)
    {
        int byteSize = sizeof(short);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + sampleCount * byteSize);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)channelCount);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channelCount * byteSize);
        writer.Write((short)(channelCount * byteSize));
        writer.Write((short)16);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        writer.Write(sampleCount * byteSize);

        foreach (float sample in m_Samples)
        {
            short sampleValue = (short)Mathf.Clamp(sample * m_Volume * short.MaxValue, short.MinValue, short.MaxValue);
            writer.Write(sampleValue);
        }
    }

    private void CheckChange()
    {
        EditorGUI.BeginChangeCheck();
    }

    private bool EndCheck()
    {
        if (EditorGUI.EndChangeCheck())
            return true;
        return false;
    }

    private bool Unfocus()
    {
        if (EditorGUI.EndChangeCheck())
        {
            EditorGUI.FocusTextInControl("");
            return true;
        }

        return false;
    }

    private void HBegin(bool boxed)
    {
        if (boxed)
            GUILayout.BeginHorizontal(EditorStyles.helpBox);
        else
            GUILayout.BeginHorizontal();

        CheckChange();
    }

    private void HEnd()
    {
        GUILayout.EndHorizontal();
        GUILayout.Space(m_GapSize);
        GUI.backgroundColor = Color.white;

        if (EndCheck())
            m_ChangedSettings = true;
    }

    private void HEndWithoutCheck()
    {
        GUILayout.EndHorizontal();
        GUILayout.Space(m_GapSize);
        GUI.backgroundColor = Color.white;
    }

    private void VBegin()
    {
        GUILayout.BeginVertical();
    }

    
    private void VEnd()
    {
        GUILayout.EndVertical();
        GUI.backgroundColor = Color.white;
    }

    private float WidthFromRatio(int widthRatio)
    {
        return position.width * widthRatio * 0.01f;
    }

    private float HeightFromRatio(int heightRatio)
    {
        return position.height * heightRatio * 0.01f;
    }

    private void Space(float space)
    {
        GUILayout.Space(space);
    }
}

//private void HBegin(int heightRatio)
//{
//    GUILayout.BeginHorizontal(GUILayout.ExpandHeight(false), GUILayout.Height(HeightFromRatio(heightRatio)));
//}
//private void VBegin(int widthRatio)
//{
//    GUILayout.BeginVertical(GUILayout.ExpandWidth(false), GUILayout.Width(WidthFromRatio(widthRatio)));
//}

//readonly string[] m_Options = { "Volume", "Chop", "FadeIn", "FadeOut" };
//int m_SelectedOption = 0;
//m_SelectedOption = GUILayout.Toolbar(m_SelectedOption, m_Options);
//switch (m_SelectedOption)
//        {
//            case 0:
//                Chop();
//                break;
//            case 1:
//                FadeIn();
//                break;
//            case 3:
//                Volume();
//                break;
//        }

//m_LabelStyle = new GUIStyle(EditorStyles.label)
//{
//    fontStyle = FontStyle.Bold,
//    fontSize = 16,
//    normal = { textColor = Color.white },
//    alignment = TextAnchor.UpperLeft,
//};

//m_ButtonStyle = new GUIStyle(EditorStyles.miniButton)
//{
//    fixedHeight = 30f,
//    fontSize = 14,
//    fontStyle = FontStyle.Bold,
//    normal = { textColor = Color.white }
//};

//HBegin(false);
//    {
//        if (GUILayout.Button("Crush", GUILayout.Width(WidthFromRatio(m_SmallFixedRatio))))
//            Crush();
//        m_BitDepth = EditorGUILayout.IntSlider(m_BitDepth, 1, 16);
//    }
//    HEnd();

//private int m_BitDepth = 16;
//private void Crush()
//{
//    float maxSampleValue = Mathf.Pow(2, m_BitDepth) - 1;
//    for (int i = 0; i < m_Samples.Length; i++)
//        m_Samples[i] = Mathf.Round(m_Samples[i] * maxSampleValue) / maxSampleValue;
//    m_CopyClip.SetData(m_Samples, 0);
//}

//private void UpdateWaveform()
//{
//    int pointCount = 2000;
//    Vector3[] points = new Vector3[pointCount];
//    for (int i = 0; i < pointCount; i++)
//    {
//        int sampleIndex = i * (m_Samples.Length / pointCount);
//        float value = m_Samples[sampleIndex] * m_Volume;
//        float y = m_Visualiser.y + m_Visualiser.height / 2 - value * m_Visualiser.height / 2;
//        float x = m_Visualiser.x + i * m_Visualiser.width / pointCount;
//        points[i] = new Vector3(x, y);
//    }

//    Handles.color = m_Orange;
//    Handles.DrawAAPolyLine(points);
//}

//if (GUILayout.Button(m_AudioSource.isPlaying ? "Stop" : "Play"))
//    PlayOrStop();

//EditorGUILayout.LabelField("Reselect", GUILayout.Width(m_SmallLabelSize));
//m_Reselect = EditorGUILayout.Toggle(m_Reselect);