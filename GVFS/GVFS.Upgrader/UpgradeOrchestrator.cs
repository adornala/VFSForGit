using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using System;
using System.IO;

namespace GVFS.Upgrader
{
    public class UpgradeOrchestrator
    {
        private const EventLevel DefaultEventLevel = EventLevel.Informational;

        private ProductUpgrader upgrader;
        private ITracer tracer;
        private InstallerPreRunChecker preRunChecker;
        private TextWriter output;
        private TextReader input;
        private bool mount;

        public UpgradeOrchestrator(
            ProductUpgrader upgrader,
            ITracer tracer,
            InstallerPreRunChecker preRunChecker,
            TextReader input,
            TextWriter output)
        {
            this.upgrader = upgrader;
            this.tracer = tracer;
            this.preRunChecker = preRunChecker;
            this.output = output;
            this.input = input;
            this.mount = false;
            this.ExitCode = ReturnCode.Success;
        }

        public UpgradeOrchestrator()
        {
            string logFilePath = GVFSEnlistment.GetNewGVFSLogFileName(
                ProductUpgrader.GetLogDirectoryPath(),
                GVFSConstants.LogFileTypes.UpgradeProcess);
            JsonTracer jsonTracer = new JsonTracer(GVFSConstants.GVFSEtwProviderName, "UpgradeProcess");
            jsonTracer.AddLogFileEventListener(
                logFilePath,
                DefaultEventLevel,
                Keywords.Any);

            this.tracer = jsonTracer;
            this.preRunChecker = new InstallerPreRunChecker(this.tracer, GVFSConstants.UpgradeVerbMessages.GVFSUpgradeConfirm);

            string errorMessage;
            this.upgrader = ProductUpgrader.CreateUpgrader(this.tracer, out errorMessage);
            this.output = Console.Out;
            this.input = Console.In;
            this.mount = false;
            this.ExitCode = ReturnCode.Success;
        }

        public ReturnCode ExitCode { get; private set; }

        public void Execute()
        {
            string error = null;

            string mountError = null;

            if (this.TryInitialize(out error))
            {
                try
                {
                    Version newVersion = null;
                    if (!this.TryRunUpgrade(out newVersion, out error))
                    {
                        this.ExitCode = ReturnCode.GenericError;
                    }
                }
                finally
                {
                    if (!this.TryMountRepositories(out mountError))
                    {
                        mountError = Environment.NewLine + "WARNING: " + mountError;
                        this.output.WriteLine(mountError);
                    }

                    this.DeletedDownloadedAssets();
                }
            }
            else
            {
                this.ExitCode = ReturnCode.GenericError;
            }

            if (this.ExitCode == ReturnCode.GenericError)
            {
                error = Environment.NewLine + "ERROR: " + error;
                this.output.WriteLine(error);
            }
            else
            {
                this.output.WriteLine($"{Environment.NewLine}Upgrade completed successfully{(string.IsNullOrEmpty(mountError) ? "." : ", but one or more repositories will need to be mounted manually.")}");
            }

            if (this.input == Console.In)
            {
                this.output.WriteLine("Press Enter to exit.");
                this.input.ReadLine();
            }

            Environment.ExitCode = (int)this.ExitCode;
        }

        private bool LaunchInsideSpinner(Func<bool> method, string message)
        {
            return ConsoleHelper.ShowStatusWhileRunning(
                method,
                message,
                this.output,
                this.output == Console.Out && !GVFSPlatform.Instance.IsConsoleOutputRedirectedToFile(),
                null);
        }

        private bool TryInitialize(out string errorMessage)
        {
            return this.upgrader.Initialize(out errorMessage);
        }

        private bool TryRunUpgrade(out Version newVersion, out string consoleError)
        {
            newVersion = null;

            Version newGVFSVersion = null;
            GitVersion newGitVersion = null;
            string errorMessage = null;
            if (!this.LaunchInsideSpinner(
                () =>
                {
                    if (!this.TryCheckIfUpgradeAvailable(out newGVFSVersion, out errorMessage))
                    {
                        return false;
                    }

                    this.LogInstalledVersionInfo();

                    if (!this.preRunChecker.TryRunPreUpgradeChecks(out errorMessage))
                    {
                        return false;
                    }

                    if (!this.TryDownloadUpgrade(newGVFSVersion, out errorMessage))
                    {
                        return false;
                    }

                    return true;
                },
                "Downloading"))
            {
                consoleError = errorMessage;
                return false;
            }

            if (!this.LaunchInsideSpinner(
                () =>
                {
                    if (!this.preRunChecker.TryUnmountAllGVFSRepos(out errorMessage))
                    {
                        return false;
                    }

                    this.mount = true;

                    return true;
                },
                "Unmounting repositories"))
            {
                consoleError = errorMessage;
                return false;
            }

            string preInstaller;
            Version preInstallerVersion;
            if (this.upgrader.TryGetPreInstallerInfo(out preInstaller, out preInstallerVersion, out errorMessage))
            {
                if (!this.LaunchInsideSpinner(
                () =>
                {
                    if (!this.TryRunPreInstaller(preInstaller, preInstallerVersion, out errorMessage))
                    {
                        return false;
                    }

                    return true;
                },
                $"Running \"{preInstaller}\": {preInstallerVersion}"))
                {
                    consoleError = errorMessage;
                    return false;
                }
            }

            this.TryGetNewGitVersion(out newGitVersion, out errorMessage);
            if (!this.LaunchInsideSpinner(
                () =>
                {
                    if (!this.TryInstallGitUpgrade(newGitVersion, out errorMessage))
                    {
                        return false;
                    }

                    return true;
                },
                $"Installing Git version: {newGitVersion}"))
            {
                consoleError = errorMessage;
                return false;
            }

            if (!this.LaunchInsideSpinner(
                () =>
                {
                    if (!this.TryInstallGVFSUpgrade(newGVFSVersion, out errorMessage))
                    {
                        return false;
                    }

                    return true;
                },
                $"Installing GVFS version: {newGVFSVersion}"))
            {
                this.mount = false;

                consoleError = errorMessage;
                return false;
            }

            string postInstaller;
            Version postInstallerVersion;
            if (this.upgrader.TryGetPostInstallerInfo(out postInstaller, out postInstallerVersion, out errorMessage))
            {
                if (!this.LaunchInsideSpinner(
                () =>
                {
                    if (!this.TryRunPostInstaller(postInstaller, postInstallerVersion, out errorMessage))
                    {
                        return false;
                    }

                    return true;
                },
                $"Running \"{postInstaller}\": {postInstallerVersion}"))
                {
                    consoleError = errorMessage;
                    return false;
                }
            }

            this.LogVersionInfo(newGVFSVersion, newGitVersion, "Newly Installed Version");

            newVersion = newGVFSVersion;
            consoleError = null;
            return true;
        }

        private bool TryMountRepositories(out string consoleError)
        {
            string errorMessage = string.Empty;
            if (this.mount && !this.LaunchInsideSpinner(
                () =>
                {
                    string mountError;
                    if (!this.preRunChecker.TryMountAllGVFSRepos(out mountError))
                    {
                        EventMetadata metadata = new EventMetadata();
                        metadata.Add("Upgrade Step", nameof(this.TryMountRepositories));
                        metadata.Add("Mount Error", mountError);
                        this.tracer.RelatedError(metadata, $"{nameof(this.preRunChecker.TryMountAllGVFSRepos)} failed.");
                        errorMessage += mountError;
                        return false;
                    }

                    return true;
                },
                "Mounting repositories"))
            {
                consoleError = errorMessage;
                return false;
            }

            consoleError = null;
            return true;
        }

        private void DeletedDownloadedAssets()
        {
            string downloadsCleanupError;
            if (!this.upgrader.TryCleanup(out downloadsCleanupError))
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add("Upgrade Step", nameof(this.DeletedDownloadedAssets));
                metadata.Add("Download cleanup error", downloadsCleanupError);
                this.tracer.RelatedError(metadata, $"{nameof(this.DeletedDownloadedAssets)} failed.");
            }
        }

        private bool TryGetNewGitVersion(out GitVersion gitVersion, out string consoleError)
        {
            gitVersion = null;

            using (ITracer activity = this.tracer.StartActivity(nameof(this.TryGetNewGitVersion), EventLevel.Informational))
            {
                if (!this.upgrader.TryGetGitVersion(out gitVersion, out consoleError))
                {
                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("Upgrade Step", nameof(this.TryGetNewGitVersion));
                    this.tracer.RelatedError(metadata, $"{nameof(this.upgrader.TryGetGitVersion)} failed. {consoleError}");
                    return false;
                }

                activity.RelatedInfo("Successfully read Git version {0}", gitVersion);
            }

            return true;
        }

        private bool TryCheckIfUpgradeAvailable(out Version newestVersion, out string consoleError)
        {
            newestVersion = null;

            using (ITracer activity = this.tracer.StartActivity(nameof(this.TryCheckIfUpgradeAvailable), EventLevel.Informational))
            {
                if (!this.upgrader.TryGetNewerVersion(out newestVersion, out string _, out consoleError))
                {
                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("Upgrade Step", nameof(this.TryCheckIfUpgradeAvailable));
                    this.tracer.RelatedError(metadata, $"{nameof(this.upgrader.TryGetNewerVersion)} failed. {consoleError}");
                    return false;
                }

                if (newestVersion == null)
                {
                    consoleError = "Upgrade is not available.";
                    this.tracer.RelatedInfo("No new upgrade releases available");
                    return false;
                }

                activity.RelatedInfo("Successfully checked for new release. {0}", newestVersion);
            }

            return true;
        }

        private bool TryDownloadUpgrade(Version version, out string consoleError)
        {
            using (ITracer activity = this.tracer.StartActivity(
                $"{nameof(this.TryDownloadUpgrade)}({version.ToString()})",
                EventLevel.Informational))
            {
                if (!this.upgrader.TryDownloadNewestVersion(out consoleError))
                {
                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("Upgrade Step", nameof(this.TryDownloadUpgrade));
                    this.tracer.RelatedError(metadata, $"{nameof(this.upgrader.TryDownloadNewestVersion)} failed. {consoleError}");
                    return false;
                }

                activity.RelatedInfo("Successfully downloaded version: " + version.ToString());
            }

            return true;
        }

        private bool TryRunPreInstaller(string name, Version version, out string consoleError)
        {
            bool installSuccess = false;
            using (ITracer activity = this.tracer.StartActivity(
                $"{nameof(this.TryRunPreInstaller)}({version.ToString()})",
                EventLevel.Informational))
            {
                if (!this.upgrader.TryRunPreInstaller(out installSuccess, out consoleError) ||
                    !installSuccess)
                {
                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("Upgrade Step", nameof(this.TryRunPreInstaller));
                    this.tracer.RelatedError(metadata, $"{nameof(this.upgrader.TryRunPreInstaller)} failed. {consoleError}");
                    return false;
                }

                activity.RelatedInfo($"Successfully run \"{name}\": {version.ToString()}");
            }

            return installSuccess;
        }

        private bool TryInstallGitUpgrade(GitVersion version, out string consoleError)
        {
            bool installSuccess = false;
            using (ITracer activity = this.tracer.StartActivity(
                $"{nameof(this.TryInstallGitUpgrade)}({version.ToString()})",
                EventLevel.Informational))
            {
                if (!this.upgrader.TryRunGitInstaller(out installSuccess, out consoleError) ||
                    !installSuccess)
                {
                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("Upgrade Step", nameof(this.TryInstallGitUpgrade));
                    this.tracer.RelatedError(metadata, $"{nameof(this.upgrader.TryRunGitInstaller)} failed. {consoleError}");
                    return false;
                }

                activity.RelatedInfo("Successfully installed Git version: " + version.ToString());
            }

            return installSuccess;
        }

        private bool TryInstallGVFSUpgrade(Version version, out string consoleError)
        {
            bool installSuccess = false;
            using (ITracer activity = this.tracer.StartActivity(
                $"{nameof(this.TryInstallGVFSUpgrade)}({version.ToString()})",
                EventLevel.Informational))
            {
                if (!this.upgrader.TryRunGVFSInstaller(out installSuccess, out consoleError) ||
                !installSuccess)
                {
                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("Upgrade Step", nameof(this.TryInstallGVFSUpgrade));
                    this.tracer.RelatedError(metadata, $"{nameof(this.upgrader.TryRunGVFSInstaller)} failed. {consoleError}");
                    return false;
                }

                activity.RelatedInfo("Successfully installed GVFS version: " + version.ToString());
            }

            return installSuccess;
        }

        private bool TryRunPostInstaller(string name, Version version, out string consoleError)
        {
            bool installSuccess = false;
            using (ITracer activity = this.tracer.StartActivity(
                $"{nameof(this.TryRunPostInstaller)}({version.ToString()})",
                EventLevel.Informational))
            {
                if (!this.upgrader.TryRunPostInstaller(out installSuccess, out consoleError) ||
                    !installSuccess)
                {
                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("Upgrade Step", nameof(this.TryRunPostInstaller));
                    this.tracer.RelatedError(metadata, $"{nameof(this.upgrader.TryRunPostInstaller)} failed. {consoleError}");
                    return false;
                }

                activity.RelatedInfo($"Successfully run \"{name}\": {version.ToString()}");
            }

            return installSuccess;
        }

        private void LogVersionInfo(
            Version gvfsVersion,
            GitVersion gitVersion,
            string message)
        {
            EventMetadata metadata = new EventMetadata();
            metadata.Add(nameof(gvfsVersion), gvfsVersion.ToString());
            metadata.Add(nameof(gitVersion), gitVersion.ToString());

            this.tracer.RelatedEvent(EventLevel.Informational, message, metadata);
        }

        private void LogInstalledVersionInfo()
        {
            EventMetadata metadata = new EventMetadata();
            string installedGVFSVersion = ProcessHelper.GetCurrentProcessVersion();
            metadata.Add(nameof(installedGVFSVersion), installedGVFSVersion);

            GitVersion installedGitVersion = null;
            string error = null;
            string gitPath = GVFSPlatform.Instance.GitInstallation.GetInstalledGitBinPath();
            if (!string.IsNullOrEmpty(gitPath) && GitProcess.TryGetVersion(gitPath, out installedGitVersion, out error))
            {
                metadata.Add(nameof(installedGitVersion), installedGitVersion.ToString());
            }

            this.tracer.RelatedEvent(EventLevel.Informational, "Installed Version", metadata);
        }
    }
}