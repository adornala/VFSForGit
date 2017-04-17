using GVFS.Common.Git;
using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace GVFS.Common
{
    public class GVFSEnlistment : Enlistment
    {
        // New enlistment
        public GVFSEnlistment(string enlistmentRoot, string repoUrl, string cacheServerUrl, string gitBinPath, string gvfsHooksRoot)
            : base(
                  enlistmentRoot, 
                  Path.Combine(enlistmentRoot, GVFSConstants.WorkingDirectoryRootName), 
                  repoUrl, 
                  cacheServerUrl, 
                  gitBinPath, 
                  gvfsHooksRoot)
        {
            this.SetComputedPaths();

            // Mutex name cannot include '\' (other than the '\' after Global)
            // https://msdn.microsoft.com/en-us/library/windows/desktop/ms682411(v=vs.85).aspx 
            this.EnlistmentMutex = new Mutex(false, "Global\\" + this.NamedPipeName.Replace('\\', ':'));
        }

        // Existing, configured enlistment
        public GVFSEnlistment(string enlistmentRoot, string cacheServerUrl, string gitBinPath, string gvfsHooksRoot)
            : base(
                  enlistmentRoot, 
                  Path.Combine(enlistmentRoot, GVFSConstants.WorkingDirectoryRootName), 
                  cacheServerUrl, 
                  gitBinPath, 
                  gvfsHooksRoot)
        {
            this.SetComputedPaths();

            // Mutex name cannot include '\' (other than the '\' after Global)
            // https://msdn.microsoft.com/en-us/library/windows/desktop/ms682411(v=vs.85).aspx 
            this.EnlistmentMutex = new Mutex(false, GetMutexName(enlistmentRoot));
        }

        public Mutex EnlistmentMutex { get; }

        public string NamedPipeName { get; private set; }

        public string DotGVFSRoot { get; private set; }

        public string GVFSLogsRoot { get; private set; }

        /* TODO: Story 957530 Remove code using GVFS_HEAD with next breaking change. */
        public string GVFSHeadFile { get; private set; }

        public static GVFSEnlistment CreateFromCurrentDirectory(string cacheServerUrl, string gitBinRoot)
        {
            return CreateFromDirectory(Environment.CurrentDirectory, cacheServerUrl, gitBinRoot, null);
        }

        public static string GetMutexName(string enlistmentRoot)
        {
            string pipeName = EnlistmentUtils.GetNamedPipeName(enlistmentRoot);
            return "Global\\" + pipeName.Replace('\\', ':');
        }

        public static GVFSEnlistment CreateFromDirectory(string directory, string cacheServerUrl, string gitBinRoot, string gvfsHooksRoot)
        {
            if (Directory.Exists(directory))
            {
                string enlistmentRoot = EnlistmentUtils.GetEnlistmentRoot(directory);
                if (enlistmentRoot != null)
                {
                    return new GVFSEnlistment(enlistmentRoot, cacheServerUrl, gitBinRoot, gvfsHooksRoot);
                }
            }

            return null;
        }

        public static string ToFullPath(string originalValue, string toUseIfOriginalNullOrWhitespace)
        {
            if (string.IsNullOrWhiteSpace(originalValue))
            {
                return toUseIfOriginalNullOrWhitespace;
            }

            try
            {
                return Path.GetFullPath(originalValue);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static string GetNewGVFSLogFileName(string gvfsLogsRoot, string logFileType)
        {
            return Enlistment.GetNewLogFileName(
                gvfsLogsRoot, 
                "gvfs_" + logFileType);
        }

        public bool TrySetCacheServerUrlConfig()
        {
            GitProcess git = new Git.GitProcess(this);
            string settingName = Enlistment.GetCacheConfigSettingName(this.RepoUrl);
            return !git.SetInLocalConfig(settingName, this.CacheServerUrl).HasErrors;
        }

        public bool TryCreateEnlistmentFolders()
        {
            try
            {
                Directory.CreateDirectory(this.EnlistmentRoot);
                Directory.CreateDirectory(this.WorkingDirectoryRoot);
                this.CreateHiddenDirectory(this.DotGVFSRoot);
            }
            catch (IOException)
            {
                return false;
            }

            return true;
        }

        public string GetMostRecentGVFSLogFileName(string logFileType)
        {
            DirectoryInfo logDirectory = new DirectoryInfo(this.GVFSLogsRoot);
            if (!logDirectory.Exists)
            {
                return null;
            }

            FileInfo[] files = logDirectory.GetFiles("gvfs_" + logFileType + "_*.log");
            if (files.Length == 0)
            {
                return null;
            }

            return
                files
                .OrderByDescending(fileInfo => fileInfo.CreationTime)
                .First()
                .FullName;
        }

        /* TODO: Story 957530 Remove code using GVFS_HEAD with next breaking change. */
        public bool TryParseGVFSHeadFile(out bool fileExists, out string error, out string commitId)
        {
            fileExists = false;
            error = string.Empty;
            commitId = string.Empty;

            string gvfsHeadFile = this.GVFSHeadFile;
            if (File.Exists(gvfsHeadFile))
            {
                fileExists = true;
                try
                {
                    string gvfsHeadContents = File.ReadAllText(gvfsHeadFile);
                    GitProcess.Result objectTypeResult = new GitProcess(this).CatFileGetType(gvfsHeadContents);
                    if (objectTypeResult.HasErrors)
                    {
                        error = string.Format("Error while determining the type of the commit stored in {0}: {1}", GVFSConstants.GVFSHeadCommitName, objectTypeResult.Errors);
                        return false;
                    }
                    else if (objectTypeResult.Output.StartsWith(GVFSConstants.CatFileObjectTypeCommit))
                    {
                        commitId = gvfsHeadContents;
                        return true;
                    }

                    error = string.Format("Contents of {0}: \"{1}\" is not a commit SHA", GVFSConstants.GVFSHeadCommitName, gvfsHeadContents);
                    return false;
                }
                catch (Exception e)
                {
                    error = string.Format("Exception while parsing {0}: {1}", gvfsHeadFile, e.ToString());
                    return false;
                }
            }

            error = string.Format("File \"{0}\" not found", gvfsHeadFile);
            return false;
        }

        private void SetComputedPaths()
        {
            this.NamedPipeName = EnlistmentUtils.GetNamedPipeName(this.EnlistmentRoot);
            this.DotGVFSRoot = Path.Combine(this.EnlistmentRoot, GVFSConstants.DotGVFSPath);
            this.GVFSLogsRoot = Path.Combine(this.DotGVFSRoot, GVFSConstants.GVFSLogFolderName);
            /* TODO: Story 957530 Remove code using GVFS_HEAD with next breaking change. */
            this.GVFSHeadFile = Path.Combine(this.DotGVFSRoot, GVFSConstants.GVFSHeadCommitName);
        }

        /// <summary>
        /// Creates a hidden directory @ the given path.
        /// If directory already exists, hides it.
        /// </summary>
        /// <param name="path">Path to desired hidden directory</param>
        private void CreateHiddenDirectory(string path)
        {
            DirectoryInfo dir = Directory.CreateDirectory(path);
            dir.Attributes = FileAttributes.Hidden;
        }
    }
}
