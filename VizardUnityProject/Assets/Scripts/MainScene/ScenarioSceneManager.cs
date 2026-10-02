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
using System.IO;
using TMPro;
using UnityEngine;
using VizProtobufferMessage;
#if USE_NATIVE_FILE_BROWSER
	using Crosstales.FB;
#endif
/// <summary>
/// Handles the startup of the Vizard main scene by:
/// - Sets up and operates OpNavMainCamera controller if -NoDisplay mode is enabled
/// - Ordering the creation of scenario objects
/// - Ordering the application of user settings 
/// </summary>

public class ScenarioSceneManager : MonoBehaviour
{
	[Header("GUI Components")]
	[Tooltip("Parent GUI Canvas object")]
	public GameObject GUICanvas;
	[Tooltip("Settings Panel Manager")]
	public SettingsPanelMethods settingsPanel;
	[Tooltip("Error Console Log Panel")]
	public GameObject consoleLogPanel;
	[Tooltip("Top right status text display")]
	public TextMeshProUGUI statusText;
	
	[Header("Scenario Objects")]
	[Tooltip("Parent object for all scenario-specific objects")]
	public Transform scenarioObjectsContainer;		//Parent to all objects built for the current scenario
	private CelestialBodyFactory celestialBodyFactory; //Builds all scenario Celestial Body objects
	private SpacecraftFactory spacecraftFactory;	//Builds all scenario Spacecraft and Effector objects
	private SecondaryCamerasFactory secondaryCamerasFactory;	//Builds all instrument cameras
	
	//Live Streaming
	private DirectCommunicationController directCommController; //Handles communication with live Basilisk simulation
	
	//-NoDisplay Live Streaming mode objects
	[Header("-NoDisplay")]
	[Tooltip("Indicates Vizard is in -NoDisplay mode and will not render to window")]
	public GameObject displayDisabledSign;
	private GameObject worldCenterObject; //Parent to main camera in -NoDisplay Mode
	private OpNavMainCameraController opNavCameraController; //Controls main camera in -NoDisplay mode



/// <summary>
/// Monodevelop method called before all Start methods
/// <remarks>Used to initialize variables, create scenario objects, and apply user settings.</remarks>
/// </summary>
	void Awake ()
	{
		// If playing in Editor from Main Scene, instead of going through Startup Scene)
		// the section below is used to find a file to play back and to initialize the system
		if (!DataManager.IsLiveSim && String.IsNullOrEmpty(DataManager.FilePath))
		{
			GoodEnoughAddressables.InitializeAddressables();
			Save lastSave = DataManager.LoadUserData();
			// Only read back a recording if there actually is one. Reading an empty
			// path throws out of Awake before the scene is built, and because
			// FirstMessageBuffersReadFromFile clears the message log first, it also
			// discards messages already received from a live stream.
			if (lastSave != null && !String.IsNullOrEmpty(lastSave.lastFilePath))
			{
				DataManager.FilePath = lastSave.lastFilePath;
				MessageList.FirstMessageBuffersReadFromFile(DataManager.FilePath);
			}
			#if USE_NATIVE_FILE_BROWSER
			GameObject fileBrowser = Instantiate (Resources.Load ("Prefabs/FileBrowser") as GameObject);
			fileBrowser.GetComponent<FileBrowser>().AllowSyncCalls = true;
			#endif
		}
		
		celestialBodyFactory = GetComponentInChildren<CelestialBodyFactory>();
		spacecraftFactory = GetComponentInChildren<SpacecraftFactory>();
		secondaryCamerasFactory = GetComponentInChildren<SecondaryCamerasFactory>();
		MainCameraUtilities.MainCamera = Camera.main;
		opNavCameraController = MainCameraUtilities.MainCamera.GetComponent<OpNavMainCameraController>();
		
		DataManager.ScenarioObjectsContainer = scenarioObjectsContainer;
		PersistentUserSettings.ReadPersistentSettings();
		VizardGUISettings.GUICanvas = GUICanvas;
		VizardGUISettings.SettingsPanel = settingsPanel;
		VizardGUISettings.PanelViewMgr = GUICanvas.GetComponentInChildren<PanelViewManager>();
		VizardGUISettings.PlaybackManager = GetComponent<ItsAboutTime>();
		VizardGUISettings.ConsoleLog = consoleLogPanel;
		VizardGUISettings.PostProcessingMgr = GetComponentInChildren<PostProcessingManager>();
		WriteCurrentConfigInfoMessageToConsole();

		RenderSettings.ambientLight = Color.white;
		if (Application.isEditor) {
			Application.runInBackground = true;
		}

		if (DataManager.IsLiveSim)
		{
			directCommController = GameObject.Find("DirectComm").GetComponent<DirectCommunicationController>();
			directCommController.ConnectEventDialogManager(this.gameObject.GetComponent<EventDialogManager>());
		}
		VizardGUISettings.PanelViewMgr.SetPlayModeDependentOptions();


		string targetSettingString = "";
		VizProtobufferMessage.VizMessage.Types.VizSettingsPb mySettings = MessageList.FirstMessage.Settings;
		if (mySettings!=null)
		{
			targetSettingString = ApplyAllPreScenarioObjectUserSettings(mySettings);
		}
		
		celestialBodyFactory.CreateCelestialBodies();
		if (!CelestialBodyStateUtilities.SunMsgAvailable) {
			VizardGUISettings.UpdateErrorMessages("There was no sun position message available. A directional light has been added to the scene for planet visibility.");
			GameObject directionalLightObject = Instantiate(Resources.Load("Prefabs/NoSunMsgLight") as GameObject, DataManager.ScenarioObjectsContainer);
			directionalLightObject.name = "Directional Light (no sun msg available)";
		}
		
		spacecraftFactory.CreateAvailableSpacecraft();
		secondaryCamerasFactory.CreateInstrumentCameras();

		RenderSettings.ambientIntensity=0.3f; //Darken the ambient lighting
		
		GUICanvas.GetComponent<UserGUISettings>().ApplyUserSettings();
		
		if (DataManager.InNoDisplayMode){
			displayDisabledSign.SetActive(true);
			MainCameraUtilities.MainCamera.GetComponent<MainCameraViewManager>().enabled = false;
			MainCameraUtilities.MainCamera.GetComponent<MainCameraMovementController>().enabled = false;
			opNavCameraController.enabled = true;
			opNavCameraController.SetWorldCenterObject_NoDisplayMode();
		}
		
		SetDefaultMainCameraTarget(targetSettingString);
	}

	public void Update(){
		if (DataManager.InNoDisplayMode){
			if (VizardGUISettings.AssetLoadingComplete)
			{
				if (AtomicImageBuffer.IsRequestPending)
				{
					string spacecraftName = CameraMessageUtilities.GetCameraParentName(AtomicImageBuffer.CameraID);
					opNavCameraController.SetSpacecraftAsWorldCenterTarget_NoDisplayMode(spacecraftName);
					celestialBodyFactory.UpdateCelestialBodies();
					spacecraftFactory.UpdateAllSpacecraft();
					opNavCameraController.UpdateCameraAndCaptureImage();
				}
			}
		}
		else
		{
			if (!VizardGUISettings.AssetLoadingComplete)
			{
				statusText.gameObject.SetActive(true);
				statusText.text = VizardGUISettings.StatusText;
			}
			else
			{
				statusText.text = "";
				statusText.gameObject.SetActive(false);
			}
		}
	}
	private void OnDestroy ()
	{
		PersistentUserSettings.WritePersistentSettings();
		// Save message log to local directory
		if (DataManager.SaveMsgFileOnQuit)
		{
			MessageList.SaveMessages(DataManager.SaveMsgFileName + ".bin");
		}
	}

	private void WriteCurrentConfigInfoMessageToConsole(){
		string configInfo;
		if (DataManager.IsLiveSim)
		{
			if (DataManager.SocketIsReceiveOnly)
			{
				configInfo =
					"Live streaming messages in receive only mode with Direct Comm connection at socket address: " +
					DataManager.SocketAddress;
			}
			else
			{
				configInfo =
					"Live streaming messages in two way communication with Direct Comm connection at socket address: " +
					DataManager.SocketAddress;
			}
		}
		else
		{
			configInfo = "Playing back message file: " + DataManager.FilePath; 
		}
		VizardGUISettings.UpdateErrorMessages(configInfo);
	}
/// <summary>
/// Apply all the user settings that should be set prior to scenario object creation
/// </summary>
/// <param name="mySettings">VizMessage settings for this scenario</param>
/// <returns></returns>
	private string ApplyAllPreScenarioObjectUserSettings(VizMessage.Types.VizSettingsPb mySettings)
	{
		UserGUISettings userSettings = GUICanvas.GetComponent<UserGUISettings>();
		UserGUISettings.ApplyLabelSettings(mySettings);
		userSettings.ApplyAtmosphereSetting(mySettings);
		userSettings.ApplyOrbitLinesAndCSSettings(mySettings);
		userSettings.ApplyGroundTrackLineSettings(mySettings);
		foreach (VizMessage.Types.CustomModel objModel in mySettings.CustomModels)
		{
			string modelPath = objModel.ModelPath;
			int loadType = 1;
			if ((modelPath == "CUBE") || (modelPath == "CYLINDER") || (modelPath == "SPHERE") ||
			    (modelPath == "HI_DEF_SPHERE")||(modelPath=="CAPSULE"))
			{
				loadType = 2;
			}

			if (loadType == 1)
			{
				string fullPath = modelPath;
				if ((!DataManager.IsLiveSim)&&(modelPath.StartsWith(".")))
				{
					fullPath = Path.GetFullPath(modelPath, Path.GetDirectoryName(DataManager.FilePath));
				}

				if (File.Exists(fullPath))
				{
					VizardGUISettings.AddRemoteAssetLoadToList(modelPath, loadType);
				}
			}
		}

		string cameraTarget = mySettings.MainCameraTarget;
		if (cameraTarget != String.Empty)
		{
			foreach(VizMessage.Types.Spacecraft sc in MessageList.FirstMessage.Spacecraft)
			{
				if (sc.SpacecraftName == cameraTarget)
				{
					CelestialBodyStateUtilities.ViewIsLocal = true;
					CelestialBodyStateUtilities.ViewIsSpacecraftLocal = true;
					return mySettings.MainCameraTarget;
				}
			}

			foreach (VizMessage.Types.CelestialBody cb in MessageList.FirstMessage.CelestialBodies)
			{
				if (cb.BodyName == cameraTarget)
				{
					CelestialBodyStateUtilities.ViewIsLocal = true;
					CelestialBodyStateUtilities.ViewIsSpacecraftLocal = false;
					if ((cb.BodyName.ToLower()).Contains("sun"))
					{
						CelestialBodyStateUtilities.ViewIsLocal = false;
					}

					return mySettings.MainCameraTarget;
				}
			}
		}

		return "";
	}
/// <summary>
/// Chooses the main camera target on startup if not specified by user
/// </summary>
/// <param name="messageSetting">name of camera target set by user</param>
	private void SetDefaultMainCameraTarget(string messageSetting)
	{
		//Set the camera target string to the user setting provided
		string bodyToTarget = messageSetting;
		//Find the origin target 
		GameObject originTarget = GameObject.FindGameObjectWithTag("OriginTarget"); 
		
		//If the main camera target was not specified by user, select a main camera target
		if (bodyToTarget == "")
		{
			bodyToTarget = "OriginCameraTarget"; //Default to origin target
			
			//If there are spacecraft, choose the first spacecraft in messages to be camera target
			if (MessageList.FirstMessage.Spacecraft.Count > 0)
			{
				bodyToTarget = SpacecraftStateUtilities.SpacecraftList[0].name;
				if (MessageList.FirstMessage.CelestialBodies.Count == 0)
				{
					SpacecraftStateUtilities.SpacecraftMsgOnly = true;
					bodyToTarget = "OriginCameraTarget";
					CelestialBodyStateUtilities.CelestialBodiesList.Add(originTarget);
				}
				else
				{
					Destroy(originTarget);
				}
			}
			//If there are no spacecraft messages in scenario, choose first celestial body in messages
			else if (MessageList.FirstMessage.CelestialBodies.Count > 0)
			{
				bodyToTarget = CelestialBodyStateUtilities.CelestialBodiesList[0].name;
				VizardGUISettings.UpdateErrorMessages("No spacecraft messages were present. Displaying available celestial bodies.", true);
				Destroy(originTarget);
			}
			//If no spacecraft and no celestial bodies, alert user and startup using the origin target as the main camera target
			else
			{
				VizardGUISettings.UpdateErrorMessages("No spacecraft or celestial body messages were present, better check that sim set-up.", true);
				CelestialBodyStateUtilities.CelestialBodiesList.Add(originTarget);
			}
		}
		else
		{
			//Origin target is not needed if there is a valid main camera target
			Destroy(originTarget);
		}
		//Set the main camera target
		MainCameraUtilities.MainCamera.GetComponent<MainCameraViewManager>().ApplyVizMessageCameraTarget(bodyToTarget);

	}
}

