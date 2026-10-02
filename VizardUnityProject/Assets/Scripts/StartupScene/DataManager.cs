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
using UnityEngine;
using System.IO;
using UnityEngine.SceneManagement;

/// <summary>Manages file playback or live streaming socket settings needed to persist between scenes.</summary>
public static class DataManager
{
    private static string _filepath;          // Current playback file's path
    public static string LastDirectory;     // Last directory user loaded file from
    public static bool SaveMsgFileOnQuit = false;       // True if user wants messages saved to file on exit
    public static bool SaveFPSMetricsToFile = false;    // True if op nav rendering timing metrics should be saved to file
    private static bool _createMetricsWriteFile = true;  // True if metrics file still needs to be created
    public static bool FirstMessageDisplayed;           // True if the first message in the playback file or data stream has been displayed onscreen
    public static bool DisplayMostRecentMessage = true; // True if live streaming messages are coming in

    public static Transform ScenarioObjectsContainer;   // Scenario specific Main Scene objects are placed in this gameObject to allow for easy deletion when loading a new file


#if VIZARD_OPENXR
	public static readonly string MainSceneToLoad = "VizardVR_MainScene";
	public static readonly bool UseVR = true;
#else
    public static readonly string MainSceneToLoad = "VizardMainScene";
    public static readonly bool UseVR = false;
#endif

    public static string FilePath
    {
        get => _filepath;
        set
        {
            _filepath = value;
            LastDirectory = value;
        }
    }

    public static bool IsLiveSim { get; set; } //True if Vizard instance is connected to a streaming Basilisk scenario

    public static bool SocketIsReceiveOnly { get; set; } //True if Vizard is connected to Basilisk with Subscribe Socket, False if Vizard is connected with Response Socket

    public static string SocketAddress { get; set; } //Socket address Basilisk is streaming to Vizard across

    public static bool InNoDisplayMode { get; set; } //True if rendering to screen is turned off

    public static string SaveMsgFileName { get; set; } = "last_run"; //Desired name to save accumulated lives messages to file

    /// <summary>
    /// Where the Vizard configuration from the last run is persisted.
    /// <remarks>Stored as JSON rather than via BinaryFormatter, which is
    /// deprecated and unsupported under IL2CPP. In a WebGL build
    /// persistentDataPath is backed by IndexedDB rather than a real filesystem,
    /// but File operations against it still work.</remarks>
    /// </summary>
    private static string UserSaveDataPath =>
        Path.Combine(Application.persistentDataPath, "userData.save");

/// <summary>
/// Deactivates objects displayed for in-progress playback,
/// loads the newly selected playback file into the message buffer,
/// destroys obsolete objects, and loads a fresh copy of the Vizard Main Scene. 
/// </summary>
    public static void LoadFile()
    {
        Debug.Log($"Filepath to pass: {FilePath}");
        MessageList.PlaybackPaused = true;
        MessageList.CurrentIndex = 0;
        VizardGUISettings.OsculatingOrbitLinesVisible = false;
        VizardGUISettings.TruePathLinesVisible = false;
        VizardGUISettings.OsculatingGroundTrackOn = false;
        VizardGUISettings.TruePathGroundTrackOn = false;
        ScenarioObjectsContainer.gameObject.SetActive(false);
        VizardGUISettings.GUICanvas.SetActive(false);
        MainCameraUtilities.MainCamera.GetComponent<MainCameraMovementController>().enabled = false;
        try
        {
            CreateUserSaveData(_filepath);
            FirstMessageDisplayed = false;
            ResetAllUtilities();
            MessageList.FirstMessageBuffersReadFromFile(_filepath);
            LastDirectory = DataManager._filepath;
            SceneManager.LoadScene(MainSceneToLoad);
        }
        catch
        {
            Debug.Log($"Loading {_filepath} failed.");
        }
    }


/// <summary>
/// Write out op nav rendering timing metrics to file.
/// </summary>
/// <param name="lineToWrite">Line of metric data to append to file</param>
    public static void SaveMetrics(string lineToWrite)
    {
        _filepath = string.Format("{0}/{1}/{2}",
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "VizardData",
            "opNavMetrics.txt");
        if (_createMetricsWriteFile)
        {
            if (!Directory.Exists(Path.GetDirectoryName(_filepath)))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_filepath) ?? throw new InvalidOperationException());
            }

            if (File.Exists(_filepath))
            {
                File.Delete(_filepath);
            }

            using (StreamWriter sw = File.CreateText(_filepath))
            {
                sw.WriteLine("Seconds to Render Image, Seconds to Transmit Image");
                sw.WriteLine(lineToWrite);
            }

            _createMetricsWriteFile = false;
        }
        else
        {
            using StreamWriter sw = File.AppendText(_filepath);
            sw.WriteLine(lineToWrite);
        }
    }
/// <summary>
/// Resets all static data structures and variables when loading a different playback file
/// </summary>
    public static void ResetAllUtilities()
    {
        MainCameraUtilities.ResetMainCameraUtilities();
        LabelMaker.ResetLabelMaker();
        SpacecraftStateUtilities.ResetSpacecraftStateUtilities();
        CelestialBodyStateUtilities.ResetCelestialBodyStateUtilities();
        VizardGUISettings.ResetVizardGUISettings();

        ThrusterUtilities.ResetThrusterUtilities();
        ReactionWheelUtilities.ResetReactionWheelUtilities();

        VizInputUtilities.ResetVizInputUtilities();
    }
/// <summary>
/// Writes the last used Vizard configuration (playback vs. streaming, streaming options) to Save file.
/// </summary>
/// <param name="path"></param>
    public static void CreateUserSaveData(string path)
    {
        Save save = new Save
        {
            lastFilePath = path
        };

        if (SocketIsReceiveOnly)
        {
            save.lastCommMode = "RxOnly";
        }
        else
        {
            save.lastCommMode = "RxTx";
            save.lastDisplayMode = InNoDisplayMode ? "NoDisplay" : "LiveDisplay";
        }

        try
        {
            File.WriteAllText(UserSaveDataPath, JsonUtility.ToJson(save, true));
        }
        catch (Exception e)
        {
            //Not being able to remember the last configuration is not worth
            //interrupting startup over.
            Debug.Log($"Could not write user save data: {e.Message}");
        }
    }
/// <summary>
/// Reads save data from previous Vizard use
/// </summary>
/// <returns>Save data from the last run, or null if there is none to read</returns>
    public static Save LoadUserData()
    {
        if (!File.Exists(UserSaveDataPath))
        {
            return null;
        }

        try
        {
            return JsonUtility.FromJson<Save>(File.ReadAllText(UserSaveDataPath));
        }
        catch (Exception e)
        {
            //Also catches save files written by older Vizard versions, which
            //used BinaryFormatter. Falling back to defaults is correct here.
            Debug.Log($"Ignoring unreadable user save data: {e.Message}");
            return null;
        }
    }
}