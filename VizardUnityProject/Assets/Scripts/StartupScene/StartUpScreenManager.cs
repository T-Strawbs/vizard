/*
 ISC License

 Copyright (c) 2025, Autonomous Vehicle Systems Lab, University of Colorado at Boulder

 Permission to use, copy, modify, and/or distribute this software for any
 purpose with or without fee is hereby granted, provided that the above
 copyright notice and this permission notice appear in all copies.

 THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES
 WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF
 MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR
 ANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES
 WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN
 ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF
 OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.

 */

using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
#if USE_NATIVE_FILE_BROWSER
using Crosstales.FB;
#endif

/// <summary>
/// This class handles inputs to the VizardStartupScene UI, sets up the main scene
/// for streaming, file playback, and/or VR. 
/// </summary>
public class StartUpScreenManager : MonoBehaviour
{
    [Header("Panel GUI - File Playback")]
    public TextMeshProUGUI filepathText; // Displays user selected file for playback

    public Button selectFileButton; // Enables the file browser to select file

    [Header("Panel GUI - Streaming Options")]
    public TMP_InputField socketAddressInput; // Input field for tcp address for live connection

    public TextMeshProUGUI connectionText; // Connection toggle text
    public Toggle rxOnlyToggle; // Receive Only (Broadcast) when toggle enabled
    public Toggle rxTxToggle; // Receive and Reply (Two-Way) when toggle enabled
    public TextMeshProUGUI modeText; // Display mode toggle text
    public Toggle liveStreamingToggle; // Render Vizard Main Scene to screen
    public Toggle noDisplayToggle; // Do not render Vizard Main Scene to screen (reduces rendering cost/time)
    public TextMeshProUGUI errorText; // Display any connection or file errors

    [Header("File Browser")] [Tooltip("The file browser used for Linux builds or when NativeFileBrowser asset is not installed.")]
    public VizardFileBrowser VizardFileBrowser; // Third Party File Browser that works well with Linux platforms

    [Header("Streaming")] [Tooltip("Provides the direct comm streaming connection and message handling.")]
    public DirectCommunicationController directCommController;

    private Save lastSave; // Vizard Configuration data from last use (used to set up Startup Screen GUI)

    // How long to wait for the first scenario messages before giving up. The old
    // value of half a second suited a local ZMQ socket; a WebSocket to the bridge
    // has a handshake to complete first, so it needs materially longer.
    private const float ConnectionTimeoutSeconds = 10f;

    private readonly Color
        inactiveTextColor = new Color(0.3962f, 0.3868f, 0.3868f, 1f); //Text color for inactive GUI components

    private readonly Color
        activeTextColor = new Color(0.1960784f, 0.1960784f, 0.1960784f, 1f); //Text color for active GUI components


#if VIZARD_OPENXR
    // Sets up use of controllers for input to VizardVR_StartupScene
    // [Header("VR Input")]
    // public InputActionAsset inputActionAsset;
    // public GameObject rightRaycast;
    // public GameObject leftRaycast;
    // public Transform leftEndMarker;
    // public Transform rightEndMarker;
    //
    //
    // private InputAction _rightTrigger;
    // private InputAction _leftTrigger;
#endif
    /// <summary>
    ///  Handle any command line arguments and save data from last use
    ///  </summary>
    void Start()
    {
#if USE_NATIVE_FILE_BROWSER
        // Note: The crosstales FileBrowser prefab must have been moved into Resources>Prefabs for this instantiation to work in Vizard player
        GameObject fileBrowser = Instantiate (Resources.Load ("Prefabs/FileBrowser") as GameObject);
        fileBrowser.GetComponent<FileBrowser>().AllowSyncCalls = true;
#endif
        DataManager.FirstMessageDisplayed = false;
        Debug.Log("Resetting from Startup Scene Manager.");
        DataManager.ResetAllUtilities();
        GoodEnoughAddressables.InitializeAddressables();


        Debug.Log("My platform is: " + Application.platform);
        string[] args = Environment.GetCommandLineArgs();

        //First check for any command line settings that are NOT the run mode
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "-saveMsgFile")
            {
                DataManager.SaveMsgFileOnQuit = true;
                if (i + 1 < args.Length)
                {
                    string possibleFileName = args[i + 1];
                    if (possibleFileName.Substring(0, 1) != "-")
                    {
                        DataManager.SaveMsgFileName=possibleFileName;
                    }
                }
            }

            if (args[i] == "-saveMetrics")
            {
                DataManager.SaveFPSMetricsToFile = true;
            }
        }

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "-opNavMode" || args[i] == "-opNav" || args[i] == "-noDisplay")
            {
                string socketAddress = args[i + 1];
                socketAddressInput.text = socketAddress;
                DataManager.SocketAddress = socketAddress;
                Debug.Log("Connecting to socketAddress: " + DataManager.SocketAddress);
                SetDataManagerSettingsAndUpdateToggles(true, false, true);
                StartVisualizationButtonClicked();
                return;
            }

            if (args[i] == "-directComm")
            {
                string socketAddress = args[i + 1];
                socketAddressInput.text = socketAddress;
                DataManager.SocketAddress = socketAddress;
                Debug.Log("Connecting to SocketAddress: " + DataManager.SocketAddress);
                SetDataManagerSettingsAndUpdateToggles(true, false, false);
                StartVisualizationButtonClicked();
                return;
            }

            if (args[i] == "-loadFile")
            {
                string filepathArg = args[i + 1];
                filepathText.text = filepathArg;
                SetDataManagerSettingsAndUpdateToggles(false, false, false);
                DataManager.FilePath = filepathArg;
                StartVisualizationButtonClicked();
                return;
            }
        }

        lastSave = DataManager.LoadUserData();
        SetLastCommMethod();
        LiveConnectionTogglesInteractable(false);

        socketAddressInput.onValueChanged.AddListener(SocketAddressFieldChange);
        selectFileButton.onClick.AddListener(SelectFileButtonClicked);
    }
#if VIZARD_OPENXR
///<summary>
/// Checks for trigger in progress in each frame to enable/disable raycast
/// </summary>
    // void Update()
    // {
    //     rightRaycast.SetActive(_rightTrigger.inProgress);
    //     leftRaycast.SetActive(_leftTrigger.inProgress);
    // }
#endif
    /// <summary>
    /// Called when socket address input field text is changed.
    /// If this field is not empty or whitespace, the Connection Type
    /// and Display mode toggles are enabled.
    /// </summary>
    /// <param name="newText">New string in input field</param>
    public void SocketAddressFieldChange(string newText)
    {
        DataManager.SocketAddress = newText;
        bool isLive = !String.IsNullOrWhiteSpace(socketAddressInput.transform.GetComponent<TMP_InputField>().text);
        DataManager.IsLiveSim = isLive;
        LiveConnectionTogglesInteractable(isLive);
    }

    /// <summary>
    /// Applies command line arguments or last save data to set StartupScene GUI components
    /// </summary>
    /// <param name="useLiveSim">True if streaming is enabled</param>
    /// <param name="rxOnly">True if broadcast streaming is enabled</param>
    /// <param name="useNoDisplay">True if Vizard main scene will not render to display</param>
    private void SetDataManagerSettingsAndUpdateToggles(bool useLiveSim, bool rxOnly, bool useNoDisplay)
    {
        DataManager.IsLiveSim = useLiveSim;
        DataManager.SocketIsReceiveOnly = rxOnly;
        DataManager.InNoDisplayMode = useNoDisplay;

        rxOnlyToggle.isOn = DataManager.SocketIsReceiveOnly;
        rxTxToggle.isOn = !DataManager.SocketIsReceiveOnly;
        liveStreamingToggle.isOn = !DataManager.IsLiveSim;
        noDisplayToggle.isOn = DataManager.InNoDisplayMode;
    }

    /// <summary>
    /// Enable file browser window
    /// If Linux, use the FileChooser asset
    /// If MacOS or Windows, use the FileBrowser asset
    /// </summary>
    public void SelectFileButtonClicked()
    {
        errorText.color = Color.blue;
        errorText.text = "";
        #if USE_NATIVE_FILE_BROWSER
        if (Application.platform == RuntimePlatform.LinuxPlayer)
        {
            VizardFileBrowser.OpenFileBrowser(filepathText,"*.bin");
        }
        else
        {
            OpenSingleFileWithFileBrowser();
        }
#else
        VizardFileBrowser.OpenFileBrowser(filepathText,"*.bin");
        #endif
    }
#if USE_NATIVE_FILE_BROWSER
    /// <summary>
    /// Callback method for third party Crosstales Native File Browser
    /// </summary>
    public void OpenSingleFileWithFileBrowser()
    {
        string singleFile = FileBrowser.Instance.OpenSingleFile("Choose playback file", DataManager.LastDirectory,
            string.Empty, "bin");
        filepathText.text = singleFile;
        DataManager.FilePath = singleFile;
    }
#endif

    /// <summary>
    /// Update DataManager for user playback/streaming selections
    /// </summary>
    private void UpdateDataManagerSettings()
    {
        MessageList.ResetFirstMessage();
        if (socketAddressInput.text != "")
        {
            DataManager.IsLiveSim = true;
            LiveConnectionTogglesInteractable(true);
            DataManager.SocketIsReceiveOnly = rxOnlyToggle.isOn;
            DataManager.InNoDisplayMode = noDisplayToggle.isOn;
        }
        else
        {
            DataManager.IsLiveSim = false;
            DataManager.InNoDisplayMode = false;
        }
    }

    /// <summary>
    /// Make live connection toggles interactable when streaming address provided, inactive if empty
    /// </summary>
    /// <param name="togglesInteractive">True if streaming address provided</param>
    private void LiveConnectionTogglesInteractable(bool togglesInteractive)
    {
        Color textColor = togglesInteractive ? activeTextColor : inactiveTextColor;

        connectionText.color = textColor;
        rxOnlyToggle.interactable = togglesInteractive;
        rxOnlyToggle.transform.GetComponentInChildren<TextMeshProUGUI>().color = textColor;
        rxTxToggle.interactable = togglesInteractive;
        rxTxToggle.transform.GetComponentInChildren<TextMeshProUGUI>().color = textColor;

        modeText.color = textColor;
        liveStreamingToggle.interactable = togglesInteractive;
        liveStreamingToggle.transform.GetComponentInChildren<TextMeshProUGUI>().color = textColor;
        noDisplayToggle.interactable = togglesInteractive;
        noDisplayToggle.transform.GetComponentInChildren<TextMeshProUGUI>().color = textColor;
    }

    /// <summary>
    /// Configure DirectCommController and DataManager for user settings and trigger transition to VizardMainScene
    /// </summary>
    public void StartVisualizationButtonClicked()
    {
        UpdateDataManagerSettings();
        SaveUserData();
        errorText.color = Color.blue;

        if (DataManager.IsLiveSim)
        {
            errorText.text = "Please stand by. Establishing communication...";
            directCommController.transform.GetComponent<VizInputAccumulator>().enabled =
                !DataManager.SocketIsReceiveOnly;

            if (!directCommController.StartCommunication(DataManager.SocketAddress))
            {
                errorText.color = Color.red;
                errorText.text = "Socket failed. Please check connection address.";
                return;
            }
        }
        else if (!string.IsNullOrEmpty(filepathText.text))
        {
            errorText.text = "Please stand by. Loading scenario...";
            DataManager.FilePath = filepathText.text;
            
            // Read the binary data file
            bool readSuccess = MessageList.FirstMessageBuffersReadFromFile(DataManager.FilePath);
            if (!readSuccess)
            {
                errorText.color = Color.red;
                errorText.text = "Parsing file failed. The selected file may be corrupted.";
                return;
            }
        }
        else
        {
            errorText.color = Color.red;
            errorText.text = "You must specify a message file or socket address.";
            return;
        }

        StartCoroutine(WaitForFirstMessagesThenLoadScene());
    }

    /// <summary>
    /// Waits for the first scenario messages to arrive, then loads the main scene.
    /// </summary>
    /// <remarks>
    /// This yields rather than sleeping the main thread. The ZMQ sockets deliver on
    /// their own listener threads, so a blocking wait still saw messages arrive, but
    /// a WebGL build has no threads: messages are delivered from the browser into
    /// <see cref="DirectCommunicationController"/> during Update, which cannot run
    /// while the main thread is blocked. Sleeping here therefore guaranteed the
    /// timeout in a browser, no matter how healthy the connection was.
    /// </remarks>
    private IEnumerator WaitForFirstMessagesThenLoadScene()
    {
        float startTime = Time.realtimeSinceStartup;

        while (MessageList.TimestepsTotal < 1)
        {
            if (Time.realtimeSinceStartup - startTime > ConnectionTimeoutSeconds)
            {
                Debug.Log("Timed out waiting for messages to load.");
                if (DataManager.IsLiveSim)
                {
                    errorText.color = Color.red;
                    errorText.text = "Timed out waiting for connection. Please check address and try again.";
                    directCommController.StopSocket();
                }

                yield break;
            }

            yield return null;
        }

        if (DataManager.IsLiveSim && DataManager.SocketIsReceiveOnly)
        {
            while (!MessageList.SettingsMessageReceived)
            {
                if (Time.realtimeSinceStartup - startTime > ConnectionTimeoutSeconds)
                {
                    MessageList.SettingsMessageReceived = true;
                    VizardGUISettings.UpdateErrorMessages(
                        $"Settings message was not received within {ConnectionTimeoutSeconds} seconds of " +
                        "Receive Only live streaming and could not be applied.",
                        true);
                }

                yield return null;
            }
        }

        SceneManager.LoadScene(DataManager.MainSceneToLoad);
    }

    /// <summary>
    /// Save the current user configuration for next use
    /// </summary>
    public void SaveUserData()
    {
        string savePath = filepathText.text;
        if (savePath == "")
        {
            savePath = DataManager.LastDirectory;
        }

        DataManager.CreateUserSaveData(savePath);
    }

    /// <summary>
    /// Configure DataManager and StartupScene toggles for communication method used last by user
    /// </summary>
    public void SetLastCommMethod()
    {
        if (lastSave != null)
        {
            SetDataManagerSettingsAndUpdateToggles(false, (lastSave.lastCommMode == "RxOnly"),
                (lastSave.lastDisplayMode == "NoDisplay"));
            DataManager.LastDirectory = lastSave.lastFilePath;
        }
    }
}