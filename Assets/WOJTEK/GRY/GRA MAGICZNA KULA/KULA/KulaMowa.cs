using System.Runtime.Serialization.Formatters;
using FIMSpace;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.VFX;

[RequireComponent(typeof(AudioSource))]
public class KulaMowa : MonoBehaviour
{
    [SerializeField]
    private AudioSource audioSource;

    public bool playOnStart = false;
    public float[] spectrumData = new float[512];
    public float[] bandAverages = new float[8];
    public float[] bandBuffer = new float[8];
    public float[] bufferDecrease = new float[8];
    public VisualEffect visualEffect;
    public string[] exposedValueNames = { "BrownNote", "SubBass", "Bass", "LowMidrange", "Midrange", "UpperMidrange", "Presence", "Brilliance" };

        

    void Start()
    {
       
    }
 
    void Update()
    {
        {
            audioSource.GetSpectrumData(spectrumData, 0, FFTWindow.BlackmanHarris);
            BandBuffer();
            MakeFrequencyBands();
            ControlParticleSystem();
        }
    }

    void BandBuffer() {

    for (int g = 0; g < 8; ++g) {
        if (bandAverages [g] > bandBuffer [g])
        {
            bandBuffer [g] = bandAverages [g];
            bufferDecrease [g] = 0.005f;
        }

        if (bandAverages [g] < bandBuffer [g])
        {
            bandBuffer [g] -= bufferDecrease[g];
            bufferDecrease [g] *= 1.2f;
        }

    }
    }

    void MakeFrequencyBands()
    {
        int count = 0;
        for (int i = 0; i < 8; i++)
        {

            float average = 0;

            int sampleCount = (int)Mathf.Pow (2, i) * 2;

            if (i == 7) {
                sampleCount += 2;
            }
            for (int j = 0; j < sampleCount; j++) {
                average += spectrumData[count] * (count + 1);
                count++;

            }

            average /= count;

            bandAverages[i] = average * 10;
        }
    
    }

    void ControlParticleSystem()
    {
        // Use bandAverages to control your particle system
        // This is where you'll map the bandAverages to your particle system's parameters
        // For example: particleSystem.SetParameter("Size", bandAverages[0] * scaleFactor);

        for (int i = 0; i < bandBuffer.Length; i++)
        {
            if (visualEffect != null && i < exposedValueNames.Length)
            {
                visualEffect.SetFloat(exposedValueNames[i], bandBuffer[i]);
            }
        }
    }
}