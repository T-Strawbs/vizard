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
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using TMPro;

/// <summary>
/// Provides access to the user's file system to select
/// VizMessage scenario bins, model files, and texture files.
/// <remarks>An instance of VizardFileBrowser is included in both
/// the startup scene and the main scene</remarks>
/// </summary>
public class VizardFileBrowser : MonoBehaviour
{
    public GameObject fileBrowserPanel;
    public Transform fileButtonInventory;
    public TMP_Dropdown directoryDropdown;
    public TextMeshProUGUI selectedFileText;
    public TextMeshProUGUI errorMsgText;

    private string currentDirectory;
    private string desiredExtensionFilter = "*.bin";
    private bool firstUse = true;
    private bool isReload;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        directoryDropdown.onValueChanged.AddListener(DropdownDirectorySelected);
        fileBrowserPanel.transform.SetParent(transform.parent);
    }

    public void OpenFileBrowser(TextMeshProUGUI outText, string extString, bool isScenarioReload=false)
    {
        selectedFileText = outText;
        desiredExtensionFilter = extString;
        isReload = isScenarioReload;
        fileBrowserPanel.SetActive(true);
        fileBrowserPanel.transform.SetAsLastSibling();
        if (firstUse)
        {
            currentDirectory = Path.GetDirectoryName(DataManager.LastDirectory);
            firstUse = false;
        }
        if (currentDirectory == null)
        {
            currentDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        RefreshView();
    }

    private void RefreshView()
    {
        PopulateFileButtonInventory();
        PopulateDropdown();
    }
    
    private void PopulateFileButtonInventory()
    {
        string[] allFiles = FilterFilesForDesiredExtension();
        string[] allDirectories = Directory.GetDirectories(currentDirectory);
        CreateButtonsForFiles(allFiles, allDirectories);
    }

    private void CreateButtonsForFiles(string[] allFiles, string[] allDirectories)
    {
        ClearPreviousFileButtons();
        Vector2 sizeDelta = fileButtonInventory.GetComponent<RectTransform>().sizeDelta;
        fileButtonInventory.GetComponent<RectTransform>().sizeDelta = new Vector2(sizeDelta.x, 25 * (allFiles.Length+allDirectories.Length));
        foreach (string directory in allDirectories)
        {
            GameObject fileButton = Instantiate (Resources.Load ("Prefabs/VizardFileBrowser/FileInventoryButton") as GameObject, fileButtonInventory, false);
            fileButton.GetComponentInChildren<TextMeshProUGUI>().text = directory;
            fileButton.GetComponent<VizardFileBrowserButton>().SetButtonType("dir", this);
        }

        foreach (string file in allFiles)
        {
            string fileOnly = Path.GetFileName(file);
            GameObject fileButton = Instantiate (Resources.Load ("Prefabs/VizardFileBrowser/FileInventoryButton") as GameObject, fileButtonInventory, false);
            fileButton.GetComponentInChildren<TextMeshProUGUI>().text = fileOnly;
            fileButton.GetComponent<VizardFileBrowserButton>().SetButtonType(desiredExtensionFilter, this);
        }
        
        if (allFiles.Length+allDirectories.Length==0)
        {
            errorMsgText.text = $"No files found in current directory with extension: {desiredExtensionFilter}. Use the dropdown to navigate to a different directory.";
        }
        else
        {
            errorMsgText.text = "";
        }
    }

    private void ClearPreviousFileButtons()
    {
        int buttonCount = fileButtonInventory.childCount;
        for (int i = 0; i < buttonCount; i++)
        {
            Destroy(fileButtonInventory.GetChild(buttonCount-i-1).gameObject);
        }
    }
    
    public void FileSelected(string filename, bool isDirectory)
    {
        if (isDirectory)
        {
            currentDirectory = filename;
            RefreshView();
        }else{ 
            selectedFileText.text = Path.Combine(currentDirectory, filename);
            if (isReload)
            {
                LoadNewScenarioFile();
            }

            fileBrowserPanel.SetActive(false);
        }
    }

    private void PopulateDropdown()
    {
        List<string> directoryTree = new List<string>();
        string tempDirectory = currentDirectory;

        while (!String.IsNullOrEmpty(tempDirectory))
        {
            directoryTree.Add(tempDirectory);
            string newDirectory = Path.GetDirectoryName(tempDirectory);
            if (newDirectory == tempDirectory)
            {
                break;
            }
            tempDirectory = newDirectory;

        }

        AddAvailableDriveRoots(directoryTree);
        VizardGUISettings.PopulateList(directoryDropdown, directoryTree );
    }

    /// <summary>
    /// Appends the root of every ready drive/volume to the directory dropdown so the
    /// user can navigate off the drive the browser happens to have opened on.
    /// </summary>
    /// <remarks>Walking up from the current directory only ever yields ancestors on
    /// one drive, which on Windows left files on secondary drives unreachable.</remarks>
    private void AddAvailableDriveRoots(List<string> directoryTree)
    {
        DriveInfo[] drives;
        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch (Exception e)
        {
            Debug.Log($"Could not read the list of drives: {e.Message}");
            return;
        }

        foreach (DriveInfo drive in drives)
        {
            string driveRoot;
            try
            {
                if (!drive.IsReady)
                {
                    continue;
                }
                driveRoot = drive.RootDirectory.FullName;
            }
            catch (Exception)
            {
                //Drive disappeared or cannot be queried, skip it
                continue;
            }

            if (!directoryTree.Contains(driveRoot))
            {
                directoryTree.Add(driveRoot);
            }
        }
    }

    private void DropdownDirectorySelected(int optionValue)
    {
        currentDirectory = directoryDropdown.options[optionValue].text;
        RefreshView();
    }

    private string[] FilterFilesForDesiredExtension()
    {
        if (desiredExtensionFilter == "jpg|bmp|exr|gif|hdr|iff|pict|png|psd|tga|tiff")
        {
            //"jpg", "jpeg","bmp", "exr", "gif", "hdr", "iff", "pict", "png", "psd", "tga", "tiff"
            List<string> imageFiles = new List<string>();
            imageFiles.AddRange(Directory.GetFiles(currentDirectory, "*.jpg"));
            imageFiles.AddRange(Directory.GetFiles(currentDirectory, "*.jpeg"));
            imageFiles.AddRange(Directory.GetFiles(currentDirectory, "*.bmp"));
            imageFiles.AddRange(Directory.GetFiles(currentDirectory, "*.exr"));
            imageFiles.AddRange(Directory.GetFiles(currentDirectory, "*.gif"));
            imageFiles.AddRange(Directory.GetFiles(currentDirectory, "*.hdr"));
            imageFiles.AddRange(Directory.GetFiles(currentDirectory, "*.iff"));
            imageFiles.AddRange(Directory.GetFiles(currentDirectory, "*.pict"));
            imageFiles.AddRange(Directory.GetFiles(currentDirectory, "*.png"));
            imageFiles.AddRange(Directory.GetFiles(currentDirectory, "*.psd"));
            imageFiles.AddRange(Directory.GetFiles(currentDirectory, "*.tga"));
            imageFiles.AddRange(Directory.GetFiles(currentDirectory, "*.tif"));
            imageFiles.Sort();
            return imageFiles.ToArray();
        }
        return Directory.GetFiles(currentDirectory, desiredExtensionFilter);
    }

    private void LoadNewScenarioFile()
    {
        if (!String.IsNullOrEmpty(selectedFileText.text))
        {
            string oldFilePath = DataManager.FilePath;
            DataManager.FilePath = selectedFileText.text;

            if (File.Exists(DataManager.FilePath))
            {
                DataManager.LoadFile();
            }
            else{
                selectedFileText.text = "File load failed. Please select a different file.";
                DataManager.FilePath=oldFilePath;
            }

        }
    }
}