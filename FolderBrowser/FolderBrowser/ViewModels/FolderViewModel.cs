namespace FolderBrowser.ViewModels
{
  using System;
  using System.Collections.ObjectModel;
  using System.Diagnostics;
  using System.Globalization;
  using System.IO;
  using System.Linq;
  using System.Windows.Input;
  using FileSystemModels.Models;
  using FolderBrowser.Command;
  using FolderBrowser.ViewModels.Interfaces;
  using InplaceEditBoxLib.Events;
  using InplaceEditBoxLib.Interfaces;
  using MsgBox;
  using UserNotification.Events;
  using UserNotification.Interfaces;

  /// <summary>
  /// Implment the viewmodel for one folder entry for a collection of folders.
  /// </summary>
  public class FolderViewModel : Base.ViewModelBase, IFolderViewModel, INotifyableViewModel, IEditBox
  {
    #region fields
    /// <summary>
    /// Log4net logger facility for this class.
    /// </summary>
    protected static readonly log4net.ILog Logger = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

    private bool mIsSelected;
    private bool mIsExpanded;
    private bool mIsReadOnly;
    private FSItemType mItemType;
    private string mFolderName;
    private string mFolderPath;

    private ObservableCollection<IFolderViewModel> mFolders;

    private RelayCommand<object> mOpenInWindowsCommand;
    private RelayCommand<object> mCopyPathCommand;

    private string mVolumeLabel;
    #endregion fields

    #region constructor
    /// <summary>
    /// Parameterized <seealso cref="FolderViewModel"/> constructor
    /// </summary>
    public FolderViewModel(FSItemType itemType)
    : this()
    {
      this.mItemType = itemType;
    }

    /// <summary>
    /// Standard <seealso cref="FolderViewModel"/> constructor
    /// </summary>
    public FolderViewModel()
    {
      this.mIsExpanded = this.mIsSelected = this.IsReadOnly = false;
      this.mItemType = FSItemType.Unknown;
      this.mFolderName = this.mFolderPath = string.Empty;

      this.mFolders = null;

      this.mOpenInWindowsCommand = null;
      this.mCopyPathCommand = null;

      this.mVolumeLabel = null;
    }
    #endregion constructor

    #region events
    /// <summary>
    /// Expose an event that is triggered when the viewmodel tells its view:
    /// Here is another notification message please show it to the user.
    /// </summary>
    public event UserNotification.Events.ShowNotificationEventHandler ShowNotificationMessage;

    /// <summary>
    /// Expose an event that is triggered when the viewmodel requests its view to
    /// start the editing mode for rename this item.
    /// </summary>
    public event InplaceEditBoxLib.Events.RequestEditEventHandler RequestEdit;
    #endregion events

    #region properties
    /// <summary>
    /// Gets the name of this folder (without its root path component).
    /// </summary>
    public string FolderName
    {
      get
      {
        return this.mFolderName;
      }

      private set
      {
        if (this.mFolderName != value)
        {
          this.mFolderName = value;
          this.RaisePropertyChanged(() => this.FolderName);
          this.RaisePropertyChanged(() => this.DisplayItemString);
        }
      }
    }

    /// <summary>
    /// Get/set file system Path for this folder.
    /// </summary>
    public string FolderPath
    {
      get
      {
        return this.mFolderPath;
      }

      private set
      {
        if (this.mFolderPath != value)
        {
          this.mFolderPath = value;
          this.RaisePropertyChanged(() => this.FolderPath);
          this.RaisePropertyChanged(() => this.DisplayItemString);
        }
      }
    }

    /// <summary>
    /// Gets a folder item string for display purposes.
    /// This string can evaluete to 'C:\ (Windows)' for drives,
    /// if the 'C:\' drive was named 'Windows'.
    /// </summary>
    public string DisplayItemString
    {
      get
      {
        switch (this.ItemType)
        {
          case FSItemType.LogicalDrive:
            try
            {
              if (this.mVolumeLabel == null)
              {
                DriveInfo di = new System.IO.DriveInfo(this.FolderName);

                if (di.IsReady == true)
                  this.mVolumeLabel = di.VolumeLabel;
                else
                  return string.Format("{0} ({1})", this.FolderName, Local.Strings.STR_MSG_DEVICE_NOT_READY);
              }

              return string.Format("{0} {1}", this.FolderName, (string.IsNullOrEmpty(this.mVolumeLabel)
                                                                  ? string.Empty
                                                                  : string.Format("({0})", this.mVolumeLabel)));
            }
            catch (Exception exp)
            {
              Logger.Error("DriveInfo cannot be optained for:" + this.FolderName, exp);

              // Just return a folder name if everything else fails (drive may not be ready etc).
              return string.Format("{0} ({1})", this.FolderName, exp.Message.Trim());
            }

          case FSItemType.Folder:
          case FSItemType.Unknown:
          default:
            return this.FolderName;
        }
      }
    }

    /// <summary>
    /// Get/set observable collection of sub-folders of this folder.
    /// </summary>
    public ObservableCollection<IFolderViewModel> Folders
    {
      get
      {
        if (this.mFolders == null)
          this.mFolders = new ObservableCollection<IFolderViewModel>();

        return this.mFolders;
      }
    }

    /// <summary>
    /// Get/set whether this folder is currently selected or not.
    /// </summary>
    public bool IsSelected
    {
      get
      {
        return this.mIsSelected;
      }

      set
      {
        if (this.mIsSelected != value)
        {
          this.mIsSelected = value;

          this.RaisePropertyChanged(() => this.IsSelected);

          if (value == true)
            this.IsExpanded = true;                 // Default windows behaviour of expanding the selected folder
        }
      }
    }

    public bool IsReadOnly
    {
      get
      {
        return this.mIsReadOnly;
      }

      set
      {
        if (this.mIsReadOnly != value)
        {
          this.mIsReadOnly = value;

          this.RaisePropertyChanged(() => this.IsReadOnly);
        }
      }
    }
    
    /// <summary>
    /// Get/set whether this folder is currently expanded or not.
    /// </summary>
    public bool IsExpanded
    {
      get
      {
        return this.mIsExpanded;
      }

      set
      {
        if (this.mIsExpanded != value)
        {
          this.mIsExpanded = value;

          this.RaisePropertyChanged(() => this.IsExpanded);

          // Load all sub-folders into the Folders collection.
          this.LoadFolders();
        }
      }
    }

    /// <summary>
    /// Gets the type of this item (eg: Folder, HardDisk etc...).
    /// </summary>
    public FSItemType ItemType
    {
      get
      {
        return this.mItemType;
      }

      private set
      {
        if (this.mItemType != value)
        {
          this.mItemType = value;

          this.RaisePropertyChanged(() => this.ItemType);
        }
      }
    }

    /// <summary>
    /// Gets a command that will open the selected item with the current default application
    /// in Windows. The selected item (path to a file) is expected as FSItemVM parameter.
    /// (eg: Item is HTML file -> Open in Windows starts the web browser for viewing the HTML
    /// file if thats the currently associated Windows default application.
    /// </summary>
    public ICommand OpenInWindowsCommand
    {
      get
      {
        if (this.mOpenInWindowsCommand == null)
          this.mOpenInWindowsCommand = new RelayCommand<object>(
            (p) =>
            {
              var path = p as FolderViewModel;

              if (path == null)
                return;

              if (string.IsNullOrEmpty(path.FolderPath) == true)
                return;

              FolderViewModel.OpenInWindowsCommand_Executed(path.FolderPath);
            });

        return this.mOpenInWindowsCommand;
      }
    }

    /// <summary>
    /// Gets a command that will copy the path of an item into the Windows Clipboard.
    /// The item (path to a file) is expected as FSItemVM parameter.
    /// </summary>
    public ICommand CopyPathCommand
    {
      get
      {
        if (this.mCopyPathCommand == null)
          this.mCopyPathCommand = new RelayCommand<object>(
            (p) =>
            {
              var path = p as FolderViewModel;

              if (path == null)
                return;

              if (string.IsNullOrEmpty(path.FolderPath) == true)
                return;

              FolderViewModel.CopyPathCommand_Executed(path.FolderPath);
            });

        return this.mCopyPathCommand;
      }
    }
    #endregion properties

    #region methods
    /// <summary>
    /// Construct a <seealso cref="FolderViewModel"/> item that represents a Windows
    /// file system drive object (eg.: 'C:\').
    /// </summary>
    /// <param name="driveLetter"></param>
    /// <returns></returns>
    public static FolderViewModel ConstructDriveFolderViewModel(string driveLetter)
    {
      try
      {
        FolderViewModel f = new FolderViewModel(FSItemType.LogicalDrive)
        {
          FolderPath = driveLetter.TrimEnd('\\'),  // Assign drive letter 'C:\' to both elements
          FolderName = driveLetter.TrimEnd('\\'),
          IsReadOnly = true
        };

        return f;
      }
      catch
      {
      }

      return null;
    }

    /// <summary>
    /// Construct a <seealso cref="FolderViewModel"/> item that represents a Windows
    /// file system folder object (eg.: FolderPath = 'C:\Temp\', FolderName = 'Temp').
    /// </summary>
    /// <param name="dir"></param>
    /// <returns></returns>
    public static FolderViewModel ConstructFolderFolderViewModel(string dir)
    {
      try
      {
        string folderName = Path.GetFileName(dir);
        string folderPath = Path.GetFullPath(dir);

        FolderViewModel f = new FolderViewModel(FSItemType.Folder)
        {
          FolderName = folderName,
          FolderPath = folderPath
        };

        return f;
      }
      catch
      {
      }

      return null;
    }

    /// <summary>
    /// Rename the name of the folder into a new name.
    /// </summary>
    /// <param name="newFolderName"></param>
    public void RenameFolder(string newFolderName)
    {
      try
      {
        if (newFolderName != null)
        {
          if (System.IO.Directory.Exists(this.FolderPath))
          {
            string parent = System.IO.Directory.GetParent(this.FolderPath).FullName;

            string newFolderPathName = System.IO.Path.Combine(parent, newFolderName);

            System.IO.Directory.Move(this.FolderPath, newFolderPathName);

            this.FolderPath = newFolderPathName;
            this.FolderName = Path.GetFileName(this.FolderPath);
          }
        }
      }
      catch (Exception exp)
      {
        Logger.Error(string.Format("RenameFolder into '{0}' was not succesful.", newFolderName) , exp);

        if (this.ShowNotificationMessage != null)
        {
          this.ShowNotificationMessage(this, new ShowNotificationEvent
          (
            "Error while renaming folder",
            exp.Message,
            null
          ));
        }
      }
      finally
      {
        this.RaisePropertyChanged(() => this.FolderName);
        this.RaisePropertyChanged(() => this.FolderPath);
        this.RaisePropertyChanged(() => this.DisplayItemString);
      }
    }

    /// <summary>
    /// Call this method to request of start editing mode for renaming this item.
    /// </summary>
    /// <param name="request"></param>
    /// <returns>Returns true if event was successfully send (listener is attached), otherwise false</returns>
    public bool RequestEditMode(RequestEditEvent request)
    {
      if (this.RequestEdit != null)
      {
        this.RequestEdit(this, new RequestEdit(request));
        return true;
      }

      return false;
    }

    public IFolderViewModel CreateNewDirector()
    {
      // Compute default name for new folder
      var newDefaultFolderName = "New Folder";
      var newFolderName = newDefaultFolderName;
      var newFolderPath = newFolderName;

      try
      {
        if (System.IO.Directory.Exists(this.FolderPath) == false)
          return null;

        // Compute default name for new folder
        newFolderPath = System.IO.Path.Combine(this.FolderPath, newDefaultFolderName);

        for (int i = 1; System.IO.Directory.Exists(newFolderPath) == true; i++)
        {
          newFolderName = string.Format("{0} {1}", newDefaultFolderName, i);
          newFolderPath = System.IO.Path.Combine(this.FolderPath, newFolderName);
        }

        // Create that new folder
        System.IO.Directory.CreateDirectory(newFolderPath);

        return this.AddFolder(newFolderPath);
      }
      catch (Exception exp)
      {
        Logger.Error(string.Format("Creating new folder '{0}' was not succesful.", newFolderPath), exp);

        if (this.ShowNotificationMessage != null)
        {
          this.ShowNotificationMessage(this, new ShowNotificationEvent
          (
            "Error while creating new folder",
            exp.Message,
            null
          ));
        }
      }

      return null;
    }

    #region FileSystem Commands
    /// <summary>
    /// Convinience method to open Windows Explorer with a selected file (if it exists).
    /// Otherwise, Windows Explorer is opened in the location where the file should be at.
    /// </summary>
    /// <param name="sFileName"></param>
    /// <returns></returns>
    private static bool OpenContainingFolderCommand_Executed(string sFileName)
    {
      if (string.IsNullOrEmpty(sFileName) == true)
        return false;

      try
      {
        if (System.IO.File.Exists(sFileName) == true)
        {
          // combine the arguments together it doesn't matter if there is a space after ','
          string argument = @"/select, " + sFileName;

          System.Diagnostics.Process.Start("explorer.exe", argument);
          return true;
        }
        else
        {
          string sParentDir = string.Empty;

          if (System.IO.Directory.Exists(sFileName) == true)
            sParentDir = sFileName;
          else
            sParentDir = System.IO.Directory.GetParent(sFileName).FullName;

          if (System.IO.Directory.Exists(sParentDir) == false)
          {
            Msg.Show(string.Format(Local.Strings.STR_MSG_DIRECTORY_DOES_NOT_EXIST, sParentDir),
                                   Local.Strings.STR_MSG_ERROR_FINDING_RESOURCE,
                                   MsgBoxButtons.OK, MsgBoxImage.Error);

            return false;
          }
          else
          {
            // combine the arguments together it doesn't matter if there is a space after ','
            string argument = @"/select, " + sParentDir;

            System.Diagnostics.Process.Start("explorer.exe", argument);

            return true;
          }
        }
      }
      catch (System.Exception ex)
      {
        Msg.Show(string.Format("{0}\n'{1}'.", ex.Message, (sFileName == null ? string.Empty : sFileName)),
                  Local.Strings.STR_MSG_ERROR_FINDING_RESOURCE,
                  MsgBoxButtons.OK, MsgBoxImage.Error);

        return false;
      }
    }

    /// <summary>
    /// Opens a file with the current Windows default application.
    /// </summary>
    /// <param name="sFileName"></param>
    private static void OpenInWindowsCommand_Executed(string sFileName)
    {
      if (string.IsNullOrEmpty(sFileName) == true)
        return;

      try
      {
        Process.Start(new ProcessStartInfo(sFileName));
        ////OpenFileLocationInWindowsExplorer(whLink.NavigateUri.OriginalString);
      }
      catch (System.Exception ex)
      {
        Msg.Show(string.Format(CultureInfo.CurrentCulture, "{0}", ex.Message),
                 Local.Strings.STR_MSG_ERROR_FINDING_RESOURCE,
                 MsgBoxButtons.OK, MsgBoxImage.Error);
      }
    }

    /// <summary>
    /// Copies the given string into the Windows clipboard.
    /// </summary>
    /// <param name="sFileName"></param>
    private static void CopyPathCommand_Executed(string sFileName)
    {
      if (string.IsNullOrEmpty(sFileName) == true)
        return;

      try
      {
        System.Windows.Clipboard.SetText(sFileName);
      }
      catch
      {
      }
    }
    #endregion FileSystem Commands

    /// <summary>
    /// Load all sub-folders into the Folders collection.
    /// </summary>
    private void LoadFolders()
    {
      try
      {
        if (this.Folders.Count > 0)
          return;

        string[] dirs = null;

        string fullPath = Path.Combine(this.FolderPath, this.FolderName);

        if (this.FolderName.Contains(':'))                  // This is a drive
          fullPath = string.Concat(this.FolderName, "\\");
        else
          fullPath = this.FolderPath;

        try
        {
          dirs = Directory.GetDirectories(fullPath);
        }
        catch (Exception)
        {
        }

        this.Folders.Clear();

        if (dirs != null)
        {
          foreach (string dir in dirs)
            AddFolder(dir);
        }
      }
      catch (UnauthorizedAccessException ae)
      {
        Console.WriteLine(ae.Message);
      }
      catch (IOException ie)
      {
        Console.WriteLine(ie.Message);
      }
    }

    /// <summary>
    /// Add a new folder indicated by <paramref name="dir"/> as path
    /// into the sub-folder viewmodel collection of this folder item.
    /// </summary>
    /// <param name="dir"></param>
    /// <returns></returns>
    private FolderViewModel AddFolder(string dir)
    {
      try
      {
        DirectoryInfo di = new DirectoryInfo(dir);

        // create the sub-structure only if this is not a hidden directory
        if ((di.Attributes & FileAttributes.Hidden) != FileAttributes.Hidden)
        {
          var newFolder = FolderViewModel.ConstructFolderFolderViewModel(dir);
          this.Folders.Add(newFolder);

          return newFolder;
        }
      }
      catch (UnauthorizedAccessException ae)
      {
        Logger.Warn("Directory Access not authorized", ae);
      }
      catch (Exception e)
      {
        Logger.Warn(e);
      }

      return null;
    }
    #endregion methods
  }
}