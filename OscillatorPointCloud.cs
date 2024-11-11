/*************************************************************************************************
 *                        Spatial Audio Oscillator Cloud with UI                                   *
 *                        -------------------------------                                          *
 *                                                                                                *
 * A complete Unity system for generating an interactive 3D cloud of audio-visual oscillators.    *
 * Each point generates a sine wave tone based on its height, with full spatial audio and        *
 * real-time controls. Points smoothly animate between positions when randomized.                 *
 *                                                                                                *
 * Core Features:                                                                                 *
 * - 3D point cloud with height-based frequency mapping                                           *
 * - Full spatial audio (closer = louder, left/right panning)                                    *
 * - Real-time UI controls for all parameters                                                     *
 * - Smooth transitions between configurations                                                    *
 * - Height-based color mapping                                                                   *
 *                                                                                                *
 * Technical Details:                                                                             *
 * - Uses Unity's AudioSource spatialization                                                      *
 * - Particle system for visual representation                                                    *
 * - Real-time sine wave synthesis                                                               *
 * - Maximum 255 oscillators (Unity audio source limit)                                          *
 *                                                                                                *
 * Setup Instructions:                                                                            *
 * 1. Create an empty GameObject in your scene                                                    *
 * 2. Attach this script to the GameObject                                                        *
 * 3. No other setup needed - all materials and UI are created at runtime                         *
 *                                                                                                *
 * Runtime Controls:                                                                              *
 * - Master Volume: Overall volume of all oscillators                                             *
 * - Point Size: Visual size of particles                                                         *
 * - Min/Max Frequency: Range of possible oscillator frequencies                                  *
 * - Space Size: Size of the 3D space containing points                                          *
 * - Min/Max Distance: Distance-based volume falloff range                                        *
 * - Transition Time: Duration of position animations                                             *
 *                                                                                                *
 * Performance Notes:                                                                             *
 * - Best experienced with headphones for spatial audio                                           *
 * - Adjust min/max distances based on your scene scale                                           *
 * - Keep point count under 255 for optimal performance                                           *
 *                                                                                                *
 * Created by: Robert Alexander                                                                    *
 * Last Updated: 11/11/24                                                                          *
 *************************************************************************************************/

using UnityEngine;
using System.Collections.Generic;
using UnityEngine.UI;

public class OscillatorPointCloud : MonoBehaviour
{
    //============================================================================================
    //  OSCILLATOR POINT STRUCTURE
    //============================================================================================

    [System.Serializable]
    public class OscillatorPoint
    {
        public Vector3 position;          // Current position
        public Vector3 targetPosition;    // Position to move towards
        public Vector3 startPosition;     // Position to move from
        public float frequency;           // Current frequency in Hz
        public float amplitude;           // Volume multiplier (0-1)
        public AudioSource audioSource;   // Unity audio source component
        public double phase;              // Current phase of oscillator
        public ParticleSystem.Particle particle;  // Visual representation
        public float transitionProgress;  // 0 to 1 for position lerping
    }

    //============================================================================================
    //  VISUAL SETTINGS
    //============================================================================================

    [Header("Visual Settings")]
    private Material particleMaterial;  // Particle material for points
    [Range(0.01f, 0.5f)] public float pointSize = 0.05f;  // Size of each point
    private Gradient colorGradient;     // Color mapping for height
    private ParticleSystem particleSystem;    // Handles point rendering
    private ParticleSystem.Particle[] particles;  // Array of point particles

    //============================================================================================
    //  CLOUD CONFIGURATION
    //============================================================================================

    [Header("Cloud Settings")]
    [Range(0.1f, 10f)] public float spaceSize = 1f;      // Size of point cloud
    [Range(20f, 2000f)] public float minFrequency = 20f; // Lowest frequency
    [Range(20f, 2000f)] public float maxFrequency = 2000f; // Highest frequency
    [Range(1, 255)] public int numberOfPoints = 100;      // Number of points
    [Range(0.1f, 5f)] public float transitionTime = 1f;   // Animation duration
    [Range(0f, 1f)] public float masterVolume = 0.5f;     // Overall volume

    //============================================================================================
    //  SPATIAL AUDIO SETTINGS
    //============================================================================================

    [Header("Spatial Audio Settings")]
    [Range(0, 1)]
    public float spatialBlend = 1f;    // 1 = fully 3D spatial audio

    [Tooltip("Distance where volume starts decreasing")]
    public float minDistance = 1f;     // Points closer than this maintain full volume

    [Tooltip("Distance where volume reaches zero")]
    public float maxDistance = 10f;    // Points further than this will be silent

    [Tooltip("How volume decreases with distance")]
    private AnimationCurve falloffCurve = AnimationCurve.Linear(0, 1, 1, 0);  // Volume rolloff

    private List<OscillatorPoint> oscillators = new List<OscillatorPoint>();
    private bool isTransitioning = false;
    private float transitionStartTime;

    //============================================================================================
    //  UI ELEMENTS
    //============================================================================================

    private Canvas uiCanvas;
    private Button randomizeButton;
    private Slider sizeSlider;
    private Slider minFreqSlider;
    private Slider maxFreqSlider;
    private Slider transitionTimeSlider;
    private Slider volumeSlider;
    private Slider minDistanceSlider;
    private Slider maxDistanceSlider;

    //============================================================================================
    //  UNITY LIFECYCLE
    //============================================================================================

    void Start()
    {
        SetupParticleSystem();
        CreateParticleMaterial();
        CreateColorGradient();
        SetupUI();
        GeneratePointCloud();
    }

    void Update()
    {
        if (isTransitioning)
        {
            float elapsed = Time.time - transitionStartTime;
            float progress = elapsed / transitionTime;

            if (progress >= 1f)
            {
                progress = 1f;
                isTransitioning = false;
            }

            // Update positions and frequencies
            foreach (var oscillator in oscillators)
            {
                oscillator.transitionProgress = progress;
                oscillator.position = Vector3.Lerp(
                    oscillator.startPosition,
                    oscillator.targetPosition,
                    progress
                );

                // Update frequency based on new height
                oscillator.frequency = GetFrequencyForHeight(oscillator.position.y);

                // Update game object position for spatial audio
                oscillator.audioSource.transform.position = oscillator.position;
            }

            UpdateParticles();
        }
    }

    //============================================================================================
    //  INITIALIZATION AND UI SETUP
    //============================================================================================

    private void SetupUI()
    {
        // Create Canvas
        GameObject canvasObj = new GameObject("Controls Canvas");
        uiCanvas = canvasObj.AddComponent<Canvas>();
        uiCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler canvasScaler = canvasObj.AddComponent<CanvasScaler>();
        canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasObj.AddComponent<GraphicRaycaster>();

        float currentY = -20f;
        float spacing = 50f;

        // Create Panel
        GameObject panelObj = new GameObject("Panel");
        panelObj.transform.SetParent(uiCanvas.transform, false);
        RectTransform panelRect = panelObj.AddComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0, 1);
        panelRect.anchorMax = new Vector2(0, 1);
        panelRect.pivot = new Vector2(0, 1);
        panelRect.anchoredPosition = new Vector2(10, -10);
        panelRect.sizeDelta = new Vector2(220, 400);
        Image panelImage = panelObj.AddComponent<Image>();
        panelImage.color = new Color(0, 0, 0, 0.5f);

        // Create Randomize button at top
        randomizeButton = CreateButton("Randomize", panelObj.transform, new Vector2(10, currentY));
        randomizeButton.onClick.AddListener(RandomizeCloud);
        currentY -= spacing;

        // Create all parameter sliders
        volumeSlider = CreateSlider("Master Volume", 0f, 1f, masterVolume, panelObj.transform, new Vector2(10, currentY), value => {
            masterVolume = value;
            UpdateVolume();
        });
        currentY -= spacing;

        sizeSlider = CreateSlider("Point Size", 0.01f, 0.5f, pointSize, panelObj.transform, new Vector2(10, currentY), value => {
            pointSize = value;
            UpdateParticles();
        });
        currentY -= spacing;

        minFreqSlider = CreateSlider("Min Frequency", 20f, 2000f, minFrequency, panelObj.transform, new Vector2(10, currentY), value => {
            minFrequency = value;
            UpdateFrequencies();
        });
        currentY -= spacing;

        maxFreqSlider = CreateSlider("Max Frequency", 20f, 2000f, maxFrequency, panelObj.transform, new Vector2(10, currentY), value => {
            maxFrequency = value;
            UpdateFrequencies();
        });
        currentY -= spacing;

        minDistanceSlider = CreateSlider("Min Distance", 0.1f, 5f, minDistance, panelObj.transform, new Vector2(10, currentY), value => {
            minDistance = value;
            UpdateSpatialSettings();
        });
        currentY -= spacing;

        maxDistanceSlider = CreateSlider("Max Distance", 1f, 20f, maxDistance, panelObj.transform, new Vector2(10, currentY), value => {
            maxDistance = value;
            UpdateSpatialSettings();
        });
        currentY -= spacing;

        transitionTimeSlider = CreateSlider("Transition Time", 0.1f, 5f, transitionTime, panelObj.transform, new Vector2(10, currentY), value => {
            transitionTime = value;
        });
    }

    //============================================================================================
    //  UI CREATION HELPERS
    //============================================================================================

    private Slider CreateSlider(string label, float min, float max, float defaultValue, Transform parent, Vector2 position, System.Action<float> onValueChanged)
    {
        // Create slider container
        GameObject sliderObj = new GameObject($"{label} Slider");
        sliderObj.transform.SetParent(parent, false);

        RectTransform sliderRect = sliderObj.AddComponent<RectTransform>();
        sliderRect.sizeDelta = new Vector2(200, 40);
        sliderRect.anchoredPosition = position;

        // Create label
        GameObject labelObj = new GameObject("Label");
        labelObj.transform.SetParent(sliderObj.transform, false);
        Text labelText = labelObj.AddComponent<Text>();
        labelText.text = label;
        labelText.color = Color.white;
        labelText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        labelText.alignment = TextAnchor.MiddleLeft;
        RectTransform labelRect = labelObj.GetComponent<RectTransform>();
        labelRect.sizeDelta = new Vector2(100, 20);
        labelRect.anchorMin = new Vector2(0, 1);
        labelRect.anchorMax = new Vector2(0, 1);
        labelRect.pivot = new Vector2(0, 1);
        labelRect.anchoredPosition = new Vector2(0, 0);

        // Create value display
        GameObject valueObj = new GameObject("Value");
        valueObj.transform.SetParent(sliderObj.transform, false);
        Text valueText = valueObj.AddComponent<Text>();
        valueText.color = Color.white;
        valueText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        valueText.alignment = TextAnchor.MiddleRight;
        RectTransform valueRect = valueObj.GetComponent<RectTransform>();
        valueRect.sizeDelta = new Vector2(50, 20);
        valueRect.anchorMin = new Vector2(1, 1);
        valueRect.anchorMax = new Vector2(1, 1);
        valueRect.pivot = new Vector2(1, 1);
        valueRect.anchoredPosition = new Vector2(0, 0);

        // Create slider background
        GameObject bgObj = new GameObject("Background");
        bgObj.transform.SetParent(sliderObj.transform, false);
        Image bgImage = bgObj.AddComponent<Image>();
        bgImage.color = Color.gray;
        RectTransform bgRect = bgObj.GetComponent<RectTransform>();
        bgRect.sizeDelta = new Vector2(200, 20);
        bgRect.anchorMin = new Vector2(0, 0);
        bgRect.anchorMax = new Vector2(1, 0);
        bgRect.pivot = new Vector2(0.5f, 0);
        bgRect.anchoredPosition = new Vector2(0, -20);

        // Create slider fill area
        GameObject fillAreaObj = new GameObject("Fill Area");
        fillAreaObj.transform.SetParent(bgObj.transform, false);
        RectTransform fillAreaRect = fillAreaObj.AddComponent<RectTransform>();
        fillAreaRect.sizeDelta = new Vector2(-20, 20);
        fillAreaRect.anchorMin = new Vector2(0, 0.5f);
        fillAreaRect.anchorMax = new Vector2(1, 0.5f);
        fillAreaRect.pivot = new Vector2(0.5f, 0.5f);
        fillAreaRect.anchoredPosition = new Vector2(0, 0);

        // Create fill
        GameObject fillObj = new GameObject("Fill");
        fillObj.transform.SetParent(fillAreaObj.transform, false);
        Image fillImage = fillObj.AddComponent<Image>();
        fillImage.color = Color.green;
        RectTransform fillRect = fillObj.GetComponent<RectTransform>();
        fillRect.sizeDelta = new Vector2(0, 0);
        fillRect.anchorMin = new Vector2(0, 0);
        fillRect.anchorMax = new Vector2(1, 1);
        fillRect.pivot = new Vector2(0.5f, 0.5f);
        fillRect.anchoredPosition = new Vector2(0, 0);

        // Create handle slide area
        GameObject handleSlideAreaObj = new GameObject("Handle Slide Area");
        handleSlideAreaObj.transform.SetParent(bgObj.transform, false);
        RectTransform handleSlideAreaRect = handleSlideAreaObj.AddComponent<RectTransform>();
        handleSlideAreaRect.sizeDelta = new Vector2(-20, 0);
        handleSlideAreaRect.anchorMin = new Vector2(0, 0);
        handleSlideAreaRect.anchorMax = new Vector2(1, 1);
        handleSlideAreaRect.pivot = new Vector2(0.5f, 0.5f);
        handleSlideAreaRect.anchoredPosition = new Vector2(0, 0);

        // Create handle
        GameObject handleObj = new GameObject("Handle");
        handleObj.transform.SetParent(handleSlideAreaObj.transform, false);
        Image handleImage = handleObj.AddComponent<Image>();
        handleImage.color = Color.white;
        RectTransform handleRect = handleObj.GetComponent<RectTransform>();
        handleRect.sizeDelta = new Vector2(20, 20);
        handleRect.anchorMin = new Vector2(0.5f, 0.5f);
        handleRect.anchorMax = new Vector2(0.5f, 0.5f);
        handleRect.pivot = new Vector2(0.5f, 0.5f);
        handleRect.anchoredPosition = new Vector2(0, 0);

        // Add Slider component
        Slider slider = sliderObj.AddComponent<Slider>();
        slider.targetGraphic = handleImage;
        slider.fillRect = fillRect;
        slider.handleRect = handleRect;
        slider.minValue = min;
        slider.maxValue = max;
        slider.value = defaultValue;
        slider.wholeNumbers = false;

        // Update value display when slider changes
        slider.onValueChanged.AddListener((float value) => {
            valueText.text = value.ToString("F2");
            onValueChanged(value);
        });

        // Set initial value
        valueText.text = defaultValue.ToString("F2");

        return slider;
    }

    private Button CreateButton(string label, Transform parent, Vector2 position)
    {
        // Create button object
        GameObject buttonObj = new GameObject(label);
        buttonObj.transform.SetParent(parent, false);

        RectTransform buttonRect = buttonObj.AddComponent<RectTransform>();
        buttonRect.sizeDelta = new Vector2(200, 40);
        buttonRect.anchoredPosition = position;

        // Add Image component
        Image buttonImage = buttonObj.AddComponent<Image>();
        buttonImage.color = Color.gray;

        // Add Button component
        Button button = buttonObj.AddComponent<Button>();

        // Create Text
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(buttonObj.transform, false);
        Text buttonText = textObj.AddComponent<Text>();
        buttonText.text = label;
        buttonText.color = Color.white;
        buttonText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        buttonText.alignment = TextAnchor.MiddleCenter;
        RectTransform textRect = textObj.GetComponent<RectTransform>();
        textRect.sizeDelta = new Vector2(200, 40);
        textRect.anchorMin = new Vector2(0, 0);
        textRect.anchorMax = new Vector2(1, 1);
        textRect.pivot = new Vector2(0.5f, 0.5f);
        textRect.anchoredPosition = new Vector2(0, 0);

        return button;
    }

    //============================================================================================
    //  PARAMETER UPDATES
    //============================================================================================

    private void UpdateVolume()
    {
        foreach (var oscillator in oscillators)
        {
            if (oscillator.audioSource != null)
            {
                oscillator.audioSource.volume = masterVolume;
            }
        }
    }

    private void UpdateFrequencies()
    {
        foreach (var oscillator in oscillators)
        {
            oscillator.frequency = GetFrequencyForHeight(oscillator.position.y);
        }
    }

    private void UpdateSpatialSettings()
    {
        foreach (var oscillator in oscillators)
        {
            if (oscillator.audioSource != null)
            {
                oscillator.audioSource.minDistance = minDistance;
                oscillator.audioSource.maxDistance = maxDistance;
            }
        }
    }

    //============================================================================================
    //  PARTICLE SYSTEM SETUP
    //============================================================================================

    private void SetupParticleSystem()
    {
        // Create and configure particle system for visual representation
        particleSystem = gameObject.AddComponent<ParticleSystem>();
        var main = particleSystem.main;
        main.loop = false;
        main.playOnAwake = false;
        main.maxParticles = 255;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        // Configure particle renderer
        var renderer = particleSystem.GetComponent<ParticleSystemRenderer>();
        renderer.material = particleMaterial;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;

        particles = new ParticleSystem.Particle[255];
    }

    private void CreateParticleMaterial()
    {
        // Create material programmatically
        particleMaterial = new Material(Shader.Find("Particles/Standard Unlit"));
        particleMaterial.color = Color.white;
    }

    private void CreateColorGradient()
    {
        // Create a simple gradient from blue to red
        colorGradient = new Gradient();
        GradientColorKey[] colorKeys = new GradientColorKey[2];
        colorKeys[0].color = Color.blue;
        colorKeys[0].time = 0f;
        colorKeys[1].color = Color.red;
        colorKeys[1].time = 1f;

        GradientAlphaKey[] alphaKeys = new GradientAlphaKey[2];
        alphaKeys[0].alpha = 1f;
        alphaKeys[0].time = 0f;
        alphaKeys[1].alpha = 1f;
        alphaKeys[1].time = 1f;

        colorGradient.SetKeys(colorKeys, alphaKeys);
    }

    //============================================================================================
    //  CLOUD GENERATION AND MANAGEMENT
    //============================================================================================

    void GeneratePointCloud()
    {
        // Clear existing oscillators
        foreach (var oscillator in oscillators)
        {
            if (oscillator.audioSource != null)
                Destroy(oscillator.audioSource.gameObject);
        }
        oscillators.Clear();

        int pointCount = Mathf.Min(numberOfPoints, 255);

        for (int i = 0; i < pointCount; i++)
        {
            CreateOscillatorPoint(i, GetRandomPosition());
        }

        UpdateParticles();
    }

    private Vector3 GetRandomPosition()
    {
        return new Vector3(
            Random.Range(-spaceSize, spaceSize),
            Random.Range(-spaceSize, spaceSize),
            Random.Range(-spaceSize, spaceSize)
        );
    }

    private void CreateOscillatorPoint(int index, Vector3 position)
    {
        GameObject pointObj = new GameObject($"Oscillator_{index}");
        pointObj.transform.parent = transform;

        // Setup spatial audio source
        AudioSource source = pointObj.AddComponent<AudioSource>();
        ConfigureAudioSource(source);

        var filter = pointObj.AddComponent<OscillatorFilter>();

        OscillatorPoint oscillator = new OscillatorPoint
        {
            position = position,
            targetPosition = position,
            startPosition = position,
            frequency = GetFrequencyForHeight(position.y),
            amplitude = 0.5f,
            audioSource = source,
            phase = 0,
            transitionProgress = 1f
        };

        filter.oscillator = oscillator;
        oscillators.Add(oscillator);
        pointObj.transform.position = position;
    }

    //============================================================================================
    //  SPATIAL AUDIO CONFIGURATION
    //============================================================================================

    private void ConfigureAudioSource(AudioSource source)
    {
        // Basic audio settings
        source.playOnAwake = true;
        source.loop = true;
        source.volume = masterVolume;

        // Spatial audio configuration
        source.spatialBlend = spatialBlend;       // 1 = full 3D audio
        source.spatialize = true;                 // Enable HRTF spatialization
        source.minDistance = minDistance;         // Distance for full volume
        source.maxDistance = maxDistance;         // Distance for zero volume
        source.rolloffMode = AudioRolloffMode.Custom;
        source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, falloffCurve);

        // Advanced spatial settings
        source.spread = 0;                        // Focused point source
        source.dopplerLevel = 0f;                 // No doppler effect for oscillators
    }

    //============================================================================================
    //  VISUAL UPDATES AND UTILITIES
    //============================================================================================

    private void UpdateParticles()
    {
        for (int i = 0; i < oscillators.Count; i++)
        {
            var oscillator = oscillators[i];
            particles[i].position = oscillator.position;
            particles[i].startSize = pointSize;

            // Color based on height
            float heightNormalized = (oscillator.position.y + spaceSize) / (2 * spaceSize);
            particles[i].startColor = colorGradient.Evaluate(heightNormalized);
        }

        particleSystem.SetParticles(particles, oscillators.Count);
    }

    private float GetFrequencyForHeight(float yPosition)
    {
        float normalizedHeight = (yPosition + spaceSize) / (2 * spaceSize);
        return Mathf.Lerp(minFrequency, maxFrequency, normalizedHeight);
    }

    void OnDrawGizmosSelected()
    {
        // Draw min distance sphere
        Gizmos.color = new Color(0, 1, 0, 0.2f);
        Gizmos.DrawWireSphere(transform.position, minDistance);

        // Draw max distance sphere
        Gizmos.color = new Color(1, 0, 0, 0.2f);
        Gizmos.DrawWireSphere(transform.position, maxDistance);
    }

    //============================================================================================
    //  PUBLIC METHODS FOR UI INTERACTION
    //============================================================================================

    public void RandomizeCloud()
    {
        foreach (var oscillator in oscillators)
        {
            oscillator.startPosition = oscillator.position;
            oscillator.targetPosition = GetRandomPosition();
            oscillator.transitionProgress = 0f;
        }

        isTransitioning = true;
        transitionStartTime = Time.time;
    }
}

//============================================================================================
//  AUDIO SYNTHESIS
//============================================================================================

public class OscillatorFilter : MonoBehaviour
{
    public OscillatorPointCloud.OscillatorPoint oscillator;

    void OnAudioFilterRead(float[] data, int channels)
    {
        if (oscillator == null)
            return;

        double increment = oscillator.frequency * 2.0 * Mathf.PI / AudioSettings.outputSampleRate;

        for (int i = 0; i < data.Length; i += channels)
        {
            // Generate sine wave
            oscillator.phase += increment;
            if (oscillator.phase > 2 * Mathf.PI)
                oscillator.phase -= 2 * Mathf.PI;

            float sample = (float)(oscillator.amplitude * Mathf.Sin((float)oscillator.phase));

            // Copy to all channels
            for (int channel = 0; channel < channels; channel++)
            {
                data[i + channel] = sample;
            }
        }
    }
}
