﻿using GVFS.Common;
using GVFS.Platform.Windows;
using GVFS.Tests.Should;
using GVFS.UnitTests.Category;
using GVFS.UnitTests.Mock.Common;
using GVFS.UnitTests.Mock.FileSystem;
using GVFS.UnitTests.Mock.Git;
using GVFS.UnitTests.Mock.Virtualization.Background;
using GVFS.UnitTests.Mock.Virtualization.BlobSize;
using GVFS.UnitTests.Mock.Virtualization.Projection;
using GVFS.UnitTests.Virtual;
using GVFS.UnitTests.Windows.Mock;
using GVFS.Virtualization;
using GVFS.Virtualization.FileSystem;
using NUnit.Framework;
using ProjFS;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace GVFS.UnitTests.Windows.Virtualization
{
    [TestFixture]
    public class WindowsFileSystemVirtualizerTests : TestsWithCommonRepo
    {
        private static readonly Dictionary<HResult, FSResult> MappedHResults = new Dictionary<HResult, FSResult>()
        {
            { HResult.Ok, FSResult.Ok },
            { HResult.DirNotEmpty, FSResult.DirectoryNotEmpty },
            { HResult.FileNotFound, FSResult.FileOrPathNotFound },
            { HResult.PathNotFound, FSResult.FileOrPathNotFound },
            { (HResult)HResultExtensions.HResultFromNtStatus.IoReparseTagNotHandled, FSResult.IoReparseTagNotHandled },
            { HResult.VirtualizationInvalidOp, FSResult.VirtualizationInvalidOperation },
        };

        private static int numWorkThreads = 1;

        [TestCase]
        public void HResultToFSResultMapsHResults()
        {
            foreach (HResult result in Enum.GetValues(typeof(HResult)))
            {
                if (MappedHResults.ContainsKey(result))
                {
                    WindowsFileSystemVirtualizer.HResultToFSResult(result).ShouldEqual(MappedHResults[result]);
                }
                else
                {
                    WindowsFileSystemVirtualizer.HResultToFSResult(result).ShouldEqual(FSResult.IOError);
                }
            }
        }

        [TestCase]
        public void ClearNegativePathCache()
        {
            const uint InitialNegativePathCacheCount = 7;
            using (MockVirtualizationInstance mockVirtualization = new MockVirtualizationInstance())
            using (WindowsFileSystemVirtualizer virtualizer = new WindowsFileSystemVirtualizer(this.Repo.Context, this.Repo.GitObjects, mockVirtualization, numWorkThreads))
            {
                mockVirtualization.NegativePathCacheCount = InitialNegativePathCacheCount;
                
                uint totalEntryCount;
                virtualizer.ClearNegativePathCache(out totalEntryCount).ShouldEqual(new FileSystemResult(FSResult.Ok, (int)HResult.Ok));
                totalEntryCount.ShouldEqual(InitialNegativePathCacheCount);
            }
        }

        [TestCase]
        public void DeleteFile()
        {
            using (MockVirtualizationInstance mockVirtualization = new MockVirtualizationInstance())
            using (WindowsFileSystemVirtualizer virtualizer = new WindowsFileSystemVirtualizer(this.Repo.Context, this.Repo.GitObjects, mockVirtualization, numWorkThreads))
            {
                UpdateFailureReason failureReason = UpdateFailureReason.NoFailure;

                mockVirtualization.DeleteFileResult = HResult.Ok;
                mockVirtualization.DeleteFileUpdateFailureCause = UpdateFailureCause.NoFailure;
                virtualizer
                    .DeleteFile("test.txt", UpdatePlaceholderType.AllowReadOnly, out failureReason)
                    .ShouldEqual(new FileSystemResult(FSResult.Ok, (int)mockVirtualization.DeleteFileResult));
                failureReason.ShouldEqual((UpdateFailureReason)mockVirtualization.DeleteFileUpdateFailureCause);

                mockVirtualization.DeleteFileResult = HResult.FileNotFound;
                mockVirtualization.DeleteFileUpdateFailureCause = UpdateFailureCause.NoFailure;
                virtualizer
                    .DeleteFile("test.txt", UpdatePlaceholderType.AllowReadOnly, out failureReason)
                    .ShouldEqual(new FileSystemResult(FSResult.FileOrPathNotFound, (int)mockVirtualization.DeleteFileResult));
                failureReason.ShouldEqual((UpdateFailureReason)mockVirtualization.DeleteFileUpdateFailureCause);

                mockVirtualization.DeleteFileResult = HResult.VirtualizationInvalidOp;
                mockVirtualization.DeleteFileUpdateFailureCause = UpdateFailureCause.DirtyData;
                virtualizer
                    .DeleteFile("test.txt", UpdatePlaceholderType.AllowReadOnly, out failureReason)
                    .ShouldEqual(new FileSystemResult(FSResult.VirtualizationInvalidOperation, (int)mockVirtualization.DeleteFileResult));
                failureReason.ShouldEqual((UpdateFailureReason)mockVirtualization.DeleteFileUpdateFailureCause);
            }
        }

        [TestCase]
        public void UpdatePlaceholderIfNeeded()
        {
            using (MockVirtualizationInstance mockVirtualization = new MockVirtualizationInstance())
            using (WindowsFileSystemVirtualizer virtualizer = new WindowsFileSystemVirtualizer(this.Repo.Context, this.Repo.GitObjects, mockVirtualization, numWorkThreads))
            { 
                UpdateFailureReason failureReason = UpdateFailureReason.NoFailure;

                mockVirtualization.UpdatePlaceholderIfNeededResult = HResult.Ok;
                mockVirtualization.UpdatePlaceholderIfNeededFailureCause = UpdateFailureCause.NoFailure;
                virtualizer
                    .UpdatePlaceholderIfNeeded(
                        "test.txt",
                        DateTime.Now,
                        DateTime.Now,
                        DateTime.Now,
                        DateTime.Now,
                        0,
                        15,
                        string.Empty,
                        UpdatePlaceholderType.AllowReadOnly,
                        out failureReason)
                    .ShouldEqual(new FileSystemResult(FSResult.Ok, (int)mockVirtualization.UpdatePlaceholderIfNeededResult));
                failureReason.ShouldEqual((UpdateFailureReason)mockVirtualization.UpdatePlaceholderIfNeededFailureCause);

                mockVirtualization.UpdatePlaceholderIfNeededResult = HResult.FileNotFound;
                mockVirtualization.UpdatePlaceholderIfNeededFailureCause = UpdateFailureCause.NoFailure;
                virtualizer
                    .UpdatePlaceholderIfNeeded(
                        "test.txt",
                        DateTime.Now,
                        DateTime.Now,
                        DateTime.Now,
                        DateTime.Now,
                        0,
                        15,
                        string.Empty,
                        UpdatePlaceholderType.AllowReadOnly,
                        out failureReason)
                    .ShouldEqual(new FileSystemResult(FSResult.FileOrPathNotFound, (int)mockVirtualization.UpdatePlaceholderIfNeededResult));
                failureReason.ShouldEqual((UpdateFailureReason)mockVirtualization.UpdatePlaceholderIfNeededFailureCause);

                mockVirtualization.UpdatePlaceholderIfNeededResult = HResult.VirtualizationInvalidOp;
                mockVirtualization.UpdatePlaceholderIfNeededFailureCause = UpdateFailureCause.DirtyData;
                virtualizer
                    .UpdatePlaceholderIfNeeded(
                        "test.txt",
                        DateTime.Now,
                        DateTime.Now,
                        DateTime.Now,
                        DateTime.Now,
                        0,
                        15,
                        string.Empty,
                        UpdatePlaceholderType.AllowReadOnly,
                        out failureReason)
                    .ShouldEqual(new FileSystemResult(FSResult.VirtualizationInvalidOperation, (int)mockVirtualization.UpdatePlaceholderIfNeededResult));
                failureReason.ShouldEqual((UpdateFailureReason)mockVirtualization.UpdatePlaceholderIfNeededFailureCause);
            }
        }

        [TestCase]
        public void OnStartDirectoryEnumerationReturnsPendingWhenResultsNotInMemory()
        {
            using (MockBackgroundFileSystemTaskRunner backgroundTaskRunner = new MockBackgroundFileSystemTaskRunner())
            using (MockVirtualizationInstance mockVirtualization = new MockVirtualizationInstance())
            using (MockGitIndexProjection gitIndexProjection = new MockGitIndexProjection(new[] { "test.txt" }))
            using (WindowsFileSystemVirtualizer virtualizer = new WindowsFileSystemVirtualizer(this.Repo.Context, this.Repo.GitObjects, mockVirtualization, numWorkThreads))
            using (FileSystemCallbacks fileSystemCallbacks = new FileSystemCallbacks(
                this.Repo.Context,
                this.Repo.GitObjects,
                RepoMetadata.Instance,
                new MockBlobSizes(),
                gitIndexProjection,
                backgroundFileSystemTaskRunner: backgroundTaskRunner,
                fileSystemVirtualizer: virtualizer))
            {
                try
                {
                    string error;
                    fileSystemCallbacks.TryStart(out error).ShouldEqual(true);

                    Guid enumerationGuid = Guid.NewGuid();
                    gitIndexProjection.EnumerationInMemory = false;
                    mockVirtualization.OnStartDirectoryEnumeration(1, enumerationGuid, "test").ShouldEqual(HResult.Pending);
                    mockVirtualization.WaitForCompletionStatus().ShouldEqual(HResult.Ok);
                    mockVirtualization.OnEndDirectoryEnumeration(enumerationGuid).ShouldEqual(HResult.Ok);
                }
                finally
                {
                    fileSystemCallbacks.Stop();
                }
            }
        }

        [TestCase]
        public void OnStartDirectoryEnumerationReturnsSuccessWhenResultsInMemory()
        {
            using (MockBackgroundFileSystemTaskRunner backgroundTaskRunner = new MockBackgroundFileSystemTaskRunner())
            using (MockVirtualizationInstance mockVirtualization = new MockVirtualizationInstance())
            using (MockGitIndexProjection gitIndexProjection = new MockGitIndexProjection(new[] { "test" }))
            using (WindowsFileSystemVirtualizer virtualizer = new WindowsFileSystemVirtualizer(this.Repo.Context, this.Repo.GitObjects, mockVirtualization, numWorkThreads))
            using (FileSystemCallbacks fileSystemCallbacks = new FileSystemCallbacks(
                this.Repo.Context,
                this.Repo.GitObjects,
                RepoMetadata.Instance,
                new MockBlobSizes(),
                gitIndexProjection,
                backgroundFileSystemTaskRunner: backgroundTaskRunner,
                fileSystemVirtualizer: virtualizer))
            {
                try
                {
                    string error;
                    fileSystemCallbacks.TryStart(out error).ShouldEqual(true);

                    Guid enumerationGuid = Guid.NewGuid();
                    gitIndexProjection.EnumerationInMemory = true;
                    mockVirtualization.OnStartDirectoryEnumeration(1, enumerationGuid, "test").ShouldEqual(HResult.Ok);
                    mockVirtualization.OnEndDirectoryEnumeration(enumerationGuid).ShouldEqual(HResult.Ok);
                }
                finally
                {
                    fileSystemCallbacks.Stop();
                }
            }
        }

        [TestCase]
        public void GetPlaceholderInformationHandlerPathNotProjected()
        {
            using (MockBackgroundFileSystemTaskRunner backgroundTaskRunner = new MockBackgroundFileSystemTaskRunner())
            using (MockVirtualizationInstance mockVirtualization = new MockVirtualizationInstance())
            using (MockGitIndexProjection gitIndexProjection = new MockGitIndexProjection(new[] { "test.txt" }))
            using (WindowsFileSystemVirtualizer virtualizer = new WindowsFileSystemVirtualizer(this.Repo.Context, this.Repo.GitObjects, mockVirtualization, numWorkThreads))
            using (FileSystemCallbacks fileSystemCallbacks = new FileSystemCallbacks(
                this.Repo.Context,
                this.Repo.GitObjects,
                RepoMetadata.Instance,
                new MockBlobSizes(),
                gitIndexProjection,
                backgroundFileSystemTaskRunner: backgroundTaskRunner,
                fileSystemVirtualizer: virtualizer))
            {
                try
                {
                    string error;
                    fileSystemCallbacks.TryStart(out error).ShouldEqual(true);

                    mockVirtualization.OnGetPlaceholderInformation(1, "doesNotExist", 0, 0, 0, 0, 1, "UnitTests").ShouldEqual(HResult.FileNotFound);
                }
                finally
                {
                    fileSystemCallbacks.Stop();
                }
            }
        }

        [TestCase]
        public void GetPlaceholderInformationHandlerPathProjected()
        {
            using (MockBackgroundFileSystemTaskRunner backgroundTaskRunner = new MockBackgroundFileSystemTaskRunner())
            using (MockVirtualizationInstance mockVirtualization = new MockVirtualizationInstance())
            using (MockGitIndexProjection gitIndexProjection = new MockGitIndexProjection(new[] { "test.txt" }))
            using (WindowsFileSystemVirtualizer virtualizer = new WindowsFileSystemVirtualizer(this.Repo.Context, this.Repo.GitObjects, mockVirtualization, numWorkThreads))
            using (FileSystemCallbacks fileSystemCallbacks = new FileSystemCallbacks(
                this.Repo.Context,
                this.Repo.GitObjects,
                RepoMetadata.Instance,
                new MockBlobSizes(),
                gitIndexProjection,
                backgroundFileSystemTaskRunner: backgroundTaskRunner,
                fileSystemVirtualizer: virtualizer))
            {
                try
                {
                    string error;
                    fileSystemCallbacks.TryStart(out error).ShouldEqual(true);

                    mockVirtualization.OnGetPlaceholderInformation(1, "test.txt", 0, 0, 0, 0, 1, "UnitTests").ShouldEqual(HResult.Pending);
                    mockVirtualization.WaitForCompletionStatus().ShouldEqual(HResult.Ok);
                    mockVirtualization.CreatedPlaceholders.ShouldContain(entry => entry == "test.txt");
                    gitIndexProjection.PlaceholdersCreated.ShouldContain(entry => entry == "test.txt");
                }
                finally
                {
                    fileSystemCallbacks.Stop();
                }
            }
        }

        [TestCase]
        public void GetPlaceholderInformationHandlerCancelledBeforeSchedulingAsync()
        {
            using (MockBackgroundFileSystemTaskRunner backgroundTaskRunner = new MockBackgroundFileSystemTaskRunner())
            using (MockVirtualizationInstance mockVirtualization = new MockVirtualizationInstance())
            using (MockGitIndexProjection gitIndexProjection = new MockGitIndexProjection(new[] { "test.txt" }))
            using (WindowsFileSystemVirtualizer virtualizer = new WindowsFileSystemVirtualizer(this.Repo.Context, this.Repo.GitObjects, mockVirtualization, numWorkThreads))
            using (FileSystemCallbacks fileSystemCallbacks = new FileSystemCallbacks(
                this.Repo.Context,
                this.Repo.GitObjects,
                RepoMetadata.Instance,
                new MockBlobSizes(),
                gitIndexProjection,
                backgroundFileSystemTaskRunner: backgroundTaskRunner,
                fileSystemVirtualizer: virtualizer))
            {
                try
                {
                    string error;
                    fileSystemCallbacks.TryStart(out error).ShouldEqual(true);

                    gitIndexProjection.BlockIsPathProjected(willWaitForRequest: true);

                    Task.Run(() =>
                    {
                    // Wait for OnGetPlaceholderInformation to call IsPathProjected and then while it's blocked there
                    // call OnCancelCommand
                    gitIndexProjection.WaitForIsPathProjected();
                        mockVirtualization.OnCancelCommand(1);
                        gitIndexProjection.UnblockIsPathProjected();
                    });

                    mockVirtualization.OnGetPlaceholderInformation(1, "test.txt", 0, 0, 0, 0, 1, "UnitTests").ShouldEqual(HResult.Pending);

                    // Cancelling before GetPlaceholderInformation has registered the command results in placeholders being created
                    mockVirtualization.WaitForPlaceholderCreate();
                    gitIndexProjection.WaitForPlaceholderCreate();
                    mockVirtualization.CreatedPlaceholders.ShouldContain(entry => entry == "test.txt");
                    gitIndexProjection.PlaceholdersCreated.ShouldContain(entry => entry == "test.txt");
                }
                finally
                {
                    fileSystemCallbacks.Stop();
                }
            }
        }

        [TestCase]
        public void GetPlaceholderInformationHandlerCancelledDuringAsyncCallback()
        {
            using (MockBackgroundFileSystemTaskRunner backgroundTaskRunner = new MockBackgroundFileSystemTaskRunner())
            using (MockVirtualizationInstance mockVirtualization = new MockVirtualizationInstance())
            using (MockGitIndexProjection gitIndexProjection = new MockGitIndexProjection(new[] { "test.txt" }))
            using (WindowsFileSystemVirtualizer virtualizer = new WindowsFileSystemVirtualizer(this.Repo.Context, this.Repo.GitObjects, mockVirtualization, numWorkThreads))
            using (FileSystemCallbacks fileSystemCallbacks = new FileSystemCallbacks(
                this.Repo.Context,
                this.Repo.GitObjects,
                RepoMetadata.Instance,
                new MockBlobSizes(),
                gitIndexProjection,
                backgroundFileSystemTaskRunner: backgroundTaskRunner,
                fileSystemVirtualizer: virtualizer))
            {
                try
                {
                    string error;
                    fileSystemCallbacks.TryStart(out error).ShouldEqual(true);

                    gitIndexProjection.BlockGetProjectedFileInfo(willWaitForRequest: true);
                    mockVirtualization.OnGetPlaceholderInformation(1, "test.txt", 0, 0, 0, 0, 1, "UnitTests").ShouldEqual(HResult.Pending);
                    gitIndexProjection.WaitForGetProjectedFileInfo();
                    mockVirtualization.OnCancelCommand(1);
                    gitIndexProjection.UnblockGetProjectedFileInfo();

                    // Cancelling in the middle of GetPlaceholderInformation still allows it to create placeholders when the cancellation does not
                    // interrupt network requests                
                    mockVirtualization.WaitForPlaceholderCreate();
                    gitIndexProjection.WaitForPlaceholderCreate();
                    mockVirtualization.CreatedPlaceholders.ShouldContain(entry => entry == "test.txt");
                    gitIndexProjection.PlaceholdersCreated.ShouldContain(entry => entry == "test.txt");
                }
                finally
                {
                    fileSystemCallbacks.Stop();
                }
            }
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void GetPlaceholderInformationHandlerCancelledDuringNetworkRequest()
        {
            using (MockBackgroundFileSystemTaskRunner backgroundTaskRunner = new MockBackgroundFileSystemTaskRunner())
            using (MockVirtualizationInstance mockVirtualization = new MockVirtualizationInstance())
            using (MockGitIndexProjection gitIndexProjection = new MockGitIndexProjection(new[] { "test.txt" }))
            using (WindowsFileSystemVirtualizer virtualizer = new WindowsFileSystemVirtualizer(this.Repo.Context, this.Repo.GitObjects, mockVirtualization, numWorkThreads))
            using (FileSystemCallbacks fileSystemCallbacks = new FileSystemCallbacks(
                this.Repo.Context,
                this.Repo.GitObjects,
                RepoMetadata.Instance,
                new MockBlobSizes(),
                gitIndexProjection,
                backgroundFileSystemTaskRunner: backgroundTaskRunner,
                fileSystemVirtualizer: virtualizer))
            {
                try
                {
                    string error;
                    fileSystemCallbacks.TryStart(out error).ShouldEqual(true);

                    MockTracer mockTracker = this.Repo.Context.Tracer as MockTracer;
                    mockTracker.WaitRelatedEventName = "GetPlaceholderInformationAsyncHandler_GetProjectedFileInfo_Cancelled";
                    gitIndexProjection.ThrowOperationCanceledExceptionOnProjectionRequest = true;
                    mockVirtualization.OnGetPlaceholderInformation(1, "test.txt", 0, 0, 0, 0, 1, "UnitTests").ShouldEqual(HResult.Pending);

                    // Cancelling in the middle of GetPlaceholderInformation in the middle of a network request should not result in placeholder
                    // getting created
                    mockTracker.WaitForRelatedEvent();
                    mockVirtualization.CreatedPlaceholders.ShouldNotContain(entry => entry == "test.txt");
                    gitIndexProjection.PlaceholdersCreated.ShouldNotContain(entry => entry == "test.txt");
                }
                finally
                {
                    fileSystemCallbacks.Stop();
                }
            }
        }

        [TestCase]
        public void OnGetFileStreamReturnsInternalErrorWhenOffsetNonZero()
        {
            using (MockBackgroundFileSystemTaskRunner backgroundTaskRunner = new MockBackgroundFileSystemTaskRunner())
            using (MockVirtualizationInstance mockVirtualization = new MockVirtualizationInstance())
            using (MockGitIndexProjection gitIndexProjection = new MockGitIndexProjection(new[] { "test.txt" }))
            using (WindowsFileSystemVirtualizer virtualizer = new WindowsFileSystemVirtualizer(this.Repo.Context, this.Repo.GitObjects, mockVirtualization, numWorkThreads))
            using (FileSystemCallbacks fileSystemCallbacks = new FileSystemCallbacks(
                this.Repo.Context,
                this.Repo.GitObjects,
                RepoMetadata.Instance,
                new MockBlobSizes(),
                gitIndexProjection,
                backgroundFileSystemTaskRunner: backgroundTaskRunner,
                fileSystemVirtualizer: virtualizer))
            {
                try
                {
                    string error;
                    fileSystemCallbacks.TryStart(out error).ShouldEqual(true);

                    Guid enumerationGuid = Guid.NewGuid();

                    byte[] contentId = FileSystemVirtualizer.ConvertShaToContentId("0123456789012345678901234567890123456789");
                    byte[] placeholderVersion = WindowsFileSystemVirtualizer.PlaceholderVersionId;

                    mockVirtualization.OnGetFileStream(
                        commandId: 1,
                        relativePath: "test.txt",
                        byteOffset: 10,
                        length: 100,
                        streamGuid: Guid.NewGuid(),
                        contentId: contentId,
                        providerId: placeholderVersion,
                        triggeringProcessId: 2,
                        triggeringProcessImageFileName: "UnitTest").ShouldEqual(HResult.InternalError);
                }
                finally
                {
                    fileSystemCallbacks.Stop();
                }
            }
        }

        [TestCase]
        public void OnGetFileStreamReturnsInternalErrorWhenPlaceholderVersionDoesNotMatchExpected()
        {
            using (MockBackgroundFileSystemTaskRunner backgroundTaskRunner = new MockBackgroundFileSystemTaskRunner())
            using (MockVirtualizationInstance mockVirtualization = new MockVirtualizationInstance())
            using (MockGitIndexProjection gitIndexProjection = new MockGitIndexProjection(new[] { "test.txt" }))
            using (WindowsFileSystemVirtualizer virtualizer = new WindowsFileSystemVirtualizer(this.Repo.Context, this.Repo.GitObjects, mockVirtualization, numWorkThreads))
            using (FileSystemCallbacks fileSystemCallbacks = new FileSystemCallbacks(
                this.Repo.Context,
                this.Repo.GitObjects,
                RepoMetadata.Instance,
                new MockBlobSizes(),
                gitIndexProjection,
                backgroundFileSystemTaskRunner: backgroundTaskRunner,
                fileSystemVirtualizer: virtualizer))
            {
                try
                {
                    string error;
                    fileSystemCallbacks.TryStart(out error).ShouldEqual(true);

                    Guid enumerationGuid = Guid.NewGuid();

                    byte[] contentId = FileSystemVirtualizer.ConvertShaToContentId("0123456789012345678901234567890123456789");
                    byte[] epochId = new byte[] { FileSystemVirtualizer.PlaceholderVersion + 1 };

                    mockVirtualization.OnGetFileStream(
                        commandId: 1,
                        relativePath: "test.txt",
                        byteOffset: 0,
                        length: 100,
                        streamGuid: Guid.NewGuid(),
                        contentId: contentId,
                        providerId: epochId,
                        triggeringProcessId: 2,
                        triggeringProcessImageFileName: "UnitTest").ShouldEqual(HResult.InternalError);
                }
                finally
                {
                    fileSystemCallbacks.Stop();
                }
            }
        }

        [TestCase]
        public void MoveFileIntoDotGitDirectory()
        {
            using (MockBackgroundFileSystemTaskRunner backgroundTaskRunner = new MockBackgroundFileSystemTaskRunner())
            using (MockVirtualizationInstance mockVirtualization = new MockVirtualizationInstance())
            using (MockGitIndexProjection gitIndexProjection = new MockGitIndexProjection(new[] { "test.txt" }))
            using (WindowsFileSystemVirtualizer virtualizer = new WindowsFileSystemVirtualizer(this.Repo.Context, this.Repo.GitObjects, mockVirtualization, numWorkThreads))
            using (MockFileSystemCallbacks fileSystemCallbacks = new MockFileSystemCallbacks(
                this.Repo.Context,
                this.Repo.GitObjects,
                RepoMetadata.Instance,
                new MockBlobSizes(),
                gitIndexProjection,
                backgroundFileSystemTaskRunner: backgroundTaskRunner,
                fileSystemVirtualizer: virtualizer))
            {
                try
                {
                    fileSystemCallbacks.TryStart(out string error).ShouldEqual(true);

                    NotificationType notificationType = NotificationType.UseExistingMask;
                    mockVirtualization.OnNotifyFileRenamed("test.txt", Path.Combine(".git", "test.txt"), isDirectory: false, notificationMask: ref notificationType);
                    fileSystemCallbacks.OnIndexFileChangeCallCount.ShouldEqual(0);
                    fileSystemCallbacks.OnLogsHeadChangeCallCount.ShouldEqual(0);
                    fileSystemCallbacks.OnFileRenamedCallCount.ShouldEqual(1);
                    fileSystemCallbacks.OnFolderRenamedCallCount.ShouldEqual(0);
                    fileSystemCallbacks.ResetCalls();

                    // We don't expect something to rename something from outside the .gitdir to the .git\index, but this
                    // verifies that we behave as expected in case that happens
                    mockVirtualization.OnNotifyFileRenamed("test.txt", Path.Combine(".git", "index"), isDirectory: false, notificationMask: ref notificationType);
                    fileSystemCallbacks.OnIndexFileChangeCallCount.ShouldEqual(1);
                    fileSystemCallbacks.OnLogsHeadChangeCallCount.ShouldEqual(0);
                    fileSystemCallbacks.OnFileRenamedCallCount.ShouldEqual(1);
                    fileSystemCallbacks.OnFolderRenamedCallCount.ShouldEqual(0);
                    fileSystemCallbacks.ResetCalls();

                    // We don't expect something to rename something from outside the .gitdir to the .git\logs\HEAD, but this
                    // verifies that we behave as expected in case that happens
                    mockVirtualization.OnNotifyFileRenamed("test.txt", Path.Combine(".git", "logs\\HEAD"), isDirectory: false, notificationMask: ref notificationType);
                    fileSystemCallbacks.OnIndexFileChangeCallCount.ShouldEqual(0);
                    fileSystemCallbacks.OnLogsHeadChangeCallCount.ShouldEqual(1);
                    fileSystemCallbacks.OnFileRenamedCallCount.ShouldEqual(1);
                    fileSystemCallbacks.OnFolderRenamedCallCount.ShouldEqual(0);
                    fileSystemCallbacks.ResetCalls();
                }
                finally
                {
                    fileSystemCallbacks.Stop();
                }
            }
        }

        [TestCase]
        public void MoveFileFromDotGitToSrc()
        {
            using (MockBackgroundFileSystemTaskRunner backgroundTaskRunner = new MockBackgroundFileSystemTaskRunner())
            using (MockVirtualizationInstance mockVirtualization = new MockVirtualizationInstance())
            using (MockGitIndexProjection gitIndexProjection = new MockGitIndexProjection(new[] { "test.txt" }))
            using (WindowsFileSystemVirtualizer virtualizer = new WindowsFileSystemVirtualizer(this.Repo.Context, this.Repo.GitObjects, mockVirtualization, numWorkThreads))
            using (MockFileSystemCallbacks fileSystemCallbacks = new MockFileSystemCallbacks(
                this.Repo.Context,
                this.Repo.GitObjects,
                RepoMetadata.Instance,
                new MockBlobSizes(),
                gitIndexProjection,
                backgroundFileSystemTaskRunner: backgroundTaskRunner,
                fileSystemVirtualizer: virtualizer))
            {
                try
                {
                    fileSystemCallbacks.TryStart(out string error).ShouldEqual(true);

                    NotificationType notificationType = NotificationType.UseExistingMask;
                    mockVirtualization.OnNotifyFileRenamed(Path.Combine(".git", "test.txt"), "test2.txt", isDirectory: false, notificationMask: ref notificationType);
                    fileSystemCallbacks.OnIndexFileChangeCallCount.ShouldEqual(0);
                    fileSystemCallbacks.OnLogsHeadChangeCallCount.ShouldEqual(0);
                    fileSystemCallbacks.OnFileRenamedCallCount.ShouldEqual(1);
                    fileSystemCallbacks.OnFolderRenamedCallCount.ShouldEqual(0);
                }
                finally
                {
                    fileSystemCallbacks.Stop();
                }
            }
        }

        [TestCase]
        public void MoveFile()
        {
            using (MockBackgroundFileSystemTaskRunner backgroundTaskRunner = new MockBackgroundFileSystemTaskRunner())
            using (MockVirtualizationInstance mockVirtualization = new MockVirtualizationInstance())
            using (MockGitIndexProjection gitIndexProjection = new MockGitIndexProjection(new[] { "test.txt" }))
            using (WindowsFileSystemVirtualizer virtualizer = new WindowsFileSystemVirtualizer(this.Repo.Context, this.Repo.GitObjects, mockVirtualization, numWorkThreads))
            using (MockFileSystemCallbacks fileSystemCallbacks = new MockFileSystemCallbacks(
                this.Repo.Context,
                this.Repo.GitObjects,
                RepoMetadata.Instance,
                new MockBlobSizes(),
                gitIndexProjection,
                backgroundFileSystemTaskRunner: backgroundTaskRunner,
                fileSystemVirtualizer: virtualizer))
            {
                try
                {
                    fileSystemCallbacks.TryStart(out string error).ShouldEqual(true);

                    NotificationType notificationType = NotificationType.UseExistingMask;
                    mockVirtualization.OnNotifyFileRenamed("test.txt", "test2.txt", isDirectory: false, notificationMask: ref notificationType);
                    fileSystemCallbacks.OnIndexFileChangeCallCount.ShouldEqual(0);
                    fileSystemCallbacks.OnLogsHeadChangeCallCount.ShouldEqual(0);
                    fileSystemCallbacks.OnFileRenamedCallCount.ShouldEqual(1);
                    fileSystemCallbacks.OnFolderRenamedCallCount.ShouldEqual(0);
                    fileSystemCallbacks.ResetCalls();

                    mockVirtualization.OnNotifyFileRenamed("test_folder_src", "test_folder_dst", isDirectory: true, notificationMask: ref notificationType);
                    fileSystemCallbacks.OnIndexFileChangeCallCount.ShouldEqual(0);
                    fileSystemCallbacks.OnLogsHeadChangeCallCount.ShouldEqual(0);
                    fileSystemCallbacks.OnFileRenamedCallCount.ShouldEqual(0);
                    fileSystemCallbacks.OnFolderRenamedCallCount.ShouldEqual(1);
                }
                finally
                {
                    fileSystemCallbacks.Stop();
                }
            }
        }

        [TestCase]
        public void OnGetFileStreamReturnsPendingAndCompletesWithSuccessWhenNoFailures()
        {
            using (MockBackgroundFileSystemTaskRunner backgroundTaskRunner = new MockBackgroundFileSystemTaskRunner())
            using (MockVirtualizationInstance mockVirtualization = new MockVirtualizationInstance())
            using (MockGitIndexProjection gitIndexProjection = new MockGitIndexProjection(new[] { "test.txt" }))
            using (WindowsFileSystemVirtualizer virtualizer = new WindowsFileSystemVirtualizer(this.Repo.Context, this.Repo.GitObjects, mockVirtualization, numWorkThreads))
            using (FileSystemCallbacks fileSystemCallbacks = new FileSystemCallbacks(
                this.Repo.Context,
                this.Repo.GitObjects,
                RepoMetadata.Instance,
                new MockBlobSizes(),
                gitIndexProjection,
                backgroundFileSystemTaskRunner: backgroundTaskRunner,
                fileSystemVirtualizer: virtualizer))
            {
                try
                {
                    string error;
                    fileSystemCallbacks.TryStart(out error).ShouldEqual(true);

                    Guid enumerationGuid = Guid.NewGuid();

                    byte[] contentId = FileSystemVirtualizer.ConvertShaToContentId("0123456789012345678901234567890123456789");
                    byte[] placeholderVersion = WindowsFileSystemVirtualizer.PlaceholderVersionId;

                    uint fileLength = 100;
                    MockGVFSGitObjects mockGVFSGitObjects = this.Repo.GitObjects as MockGVFSGitObjects;
                    mockGVFSGitObjects.FileLength = fileLength;
                    mockVirtualization.WriteFileReturnResult = HResult.Ok;

                    mockVirtualization.OnGetFileStream(
                        commandId: 1,
                        relativePath: "test.txt",
                        byteOffset: 0,
                        length: fileLength,
                        streamGuid: Guid.NewGuid(),
                        contentId: contentId,
                        providerId: placeholderVersion,
                        triggeringProcessId: 2,
                        triggeringProcessImageFileName: "UnitTest").ShouldEqual(HResult.Pending);

                    mockVirtualization.WaitForCompletionStatus().ShouldEqual(HResult.Ok);
                }
                finally
                {
                    fileSystemCallbacks.Stop();
                }
            }
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void OnGetFileStreamHandlesTryCopyBlobContentStreamThrowingOperationCanceled()
        {
            using (MockBackgroundFileSystemTaskRunner backgroundTaskRunner = new MockBackgroundFileSystemTaskRunner())
            using (MockVirtualizationInstance mockVirtualization = new MockVirtualizationInstance())
            using (MockGitIndexProjection gitIndexProjection = new MockGitIndexProjection(new[] { "test.txt" }))
            using (WindowsFileSystemVirtualizer virtualizer = new WindowsFileSystemVirtualizer(this.Repo.Context, this.Repo.GitObjects, mockVirtualization, numWorkThreads))
            using (FileSystemCallbacks fileSystemCallbacks = new FileSystemCallbacks(
                this.Repo.Context,
                this.Repo.GitObjects,
                RepoMetadata.Instance,
                new MockBlobSizes(),
                gitIndexProjection,
                backgroundFileSystemTaskRunner: backgroundTaskRunner,
                fileSystemVirtualizer: virtualizer))
            {
                try
                {
                    string error;
                    fileSystemCallbacks.TryStart(out error).ShouldEqual(true);

                    Guid enumerationGuid = Guid.NewGuid();

                    byte[] contentId = FileSystemVirtualizer.ConvertShaToContentId("0123456789012345678901234567890123456789");
                    byte[] placeholderVersion = WindowsFileSystemVirtualizer.PlaceholderVersionId;

                    MockGVFSGitObjects mockGVFSGitObjects = this.Repo.GitObjects as MockGVFSGitObjects;

                    MockTracer mockTracker = this.Repo.Context.Tracer as MockTracer;
                    mockTracker.WaitRelatedEventName = "GetFileStreamHandlerAsyncHandler_OperationCancelled";
                    mockGVFSGitObjects.CancelTryCopyBlobContentStream = true;

                    mockVirtualization.OnGetFileStream(
                        commandId: 1,
                        relativePath: "test.txt",
                        byteOffset: 0,
                        length: 100,
                        streamGuid: Guid.NewGuid(),
                        contentId: contentId,
                        providerId: placeholderVersion,
                        triggeringProcessId: 2,
                        triggeringProcessImageFileName: "UnitTest").ShouldEqual(HResult.Pending);

                    mockTracker.WaitForRelatedEvent();
                }
                finally
                {
                    fileSystemCallbacks.Stop();
                }
            }
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void OnGetFileStreamHandlesCancellationDuringWriteAction()
        {
            using (MockBackgroundFileSystemTaskRunner backgroundTaskRunner = new MockBackgroundFileSystemTaskRunner())
            using (MockVirtualizationInstance mockVirtualization = new MockVirtualizationInstance())
            using (MockGitIndexProjection gitIndexProjection = new MockGitIndexProjection(new[] { "test.txt" }))
            using (WindowsFileSystemVirtualizer virtualizer = new WindowsFileSystemVirtualizer(this.Repo.Context, this.Repo.GitObjects, mockVirtualization, numWorkThreads))
            using (FileSystemCallbacks fileSystemCallbacks = new FileSystemCallbacks(
                this.Repo.Context,
                this.Repo.GitObjects,
                RepoMetadata.Instance,
                new MockBlobSizes(),
                gitIndexProjection,
                backgroundFileSystemTaskRunner: backgroundTaskRunner,
                fileSystemVirtualizer: virtualizer))
            {
                string error;
                fileSystemCallbacks.TryStart(out error).ShouldEqual(true);

                Guid enumerationGuid = Guid.NewGuid();

                byte[] contentId = FileSystemVirtualizer.ConvertShaToContentId("0123456789012345678901234567890123456789");
                byte[] placeholderVersion = WindowsFileSystemVirtualizer.PlaceholderVersionId;

                uint fileLength = 100;
                MockGVFSGitObjects mockGVFSGitObjects = this.Repo.GitObjects as MockGVFSGitObjects;
                mockGVFSGitObjects.FileLength = fileLength;

                MockTracer mockTracker = this.Repo.Context.Tracer as MockTracer;
                mockTracker.WaitRelatedEventName = "GetFileStreamHandlerAsyncHandler_OperationCancelled";

                mockVirtualization.BlockCreateWriteBuffer(willWaitForRequest: true);
                mockVirtualization.OnGetFileStream(
                    commandId: 1,
                    relativePath: "test.txt",
                    byteOffset: 0,
                    length: fileLength,
                    streamGuid: Guid.NewGuid(),
                    contentId: contentId,
                    providerId: placeholderVersion,
                    triggeringProcessId: 2,
                    triggeringProcessImageFileName: "UnitTest").ShouldEqual(HResult.Pending);

                mockVirtualization.WaitForCreateWriteBuffer();
                mockVirtualization.OnCancelCommand(1);
                mockVirtualization.UnblockCreateWriteBuffer();
                mockTracker.WaitForRelatedEvent();

                fileSystemCallbacks.Stop();
            }
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void OnGetFileStreamHandlesWriteFailure()
        {
            using (MockBackgroundFileSystemTaskRunner backgroundTaskRunner = new MockBackgroundFileSystemTaskRunner())
            using (MockVirtualizationInstance mockVirtualization = new MockVirtualizationInstance())
            using (MockGitIndexProjection gitIndexProjection = new MockGitIndexProjection(new[] { "test.txt" }))
            using (WindowsFileSystemVirtualizer virtualizer = new WindowsFileSystemVirtualizer(this.Repo.Context, this.Repo.GitObjects, mockVirtualization, numWorkThreads))
            using (FileSystemCallbacks fileSystemCallbacks = new FileSystemCallbacks(
                this.Repo.Context,
                this.Repo.GitObjects,
                RepoMetadata.Instance,
                new MockBlobSizes(),
                gitIndexProjection,
                backgroundFileSystemTaskRunner: backgroundTaskRunner,
                fileSystemVirtualizer: virtualizer))
            {
                try
                {
                    string error;
                    fileSystemCallbacks.TryStart(out error).ShouldEqual(true);

                    Guid enumerationGuid = Guid.NewGuid();

                    byte[] contentId = FileSystemVirtualizer.ConvertShaToContentId("0123456789012345678901234567890123456789");
                    byte[] placeholderVersion = WindowsFileSystemVirtualizer.PlaceholderVersionId;

                    uint fileLength = 100;
                    MockGVFSGitObjects mockGVFSGitObjects = this.Repo.GitObjects as MockGVFSGitObjects;
                    mockGVFSGitObjects.FileLength = fileLength;

                    MockTracer mockTracker = this.Repo.Context.Tracer as MockTracer;

                    mockVirtualization.WriteFileReturnResult = HResult.InternalError;
                    mockVirtualization.OnGetFileStream(
                        commandId: 1,
                        relativePath: "test.txt",
                        byteOffset: 0,
                        length: fileLength,
                        streamGuid: Guid.NewGuid(),
                        contentId: contentId,
                        providerId: placeholderVersion,
                        triggeringProcessId: 2,
                        triggeringProcessImageFileName: "UnitTest").ShouldEqual(HResult.Pending);

                    mockVirtualization.WaitForCompletionStatus().ShouldEqual(mockVirtualization.WriteFileReturnResult);
                }
                finally
                {
                    fileSystemCallbacks.Stop();
                }
            }
        }

        [TestCase]
        [Category(CategoryConstants.ExceptionExpected)]
        public void OnGetFileStreamHandlesFileClosed()
        {
            using (MockBackgroundFileSystemTaskRunner backgroundTaskRunner = new MockBackgroundFileSystemTaskRunner())
            using (MockVirtualizationInstance mockVirtualization = new MockVirtualizationInstance())
            using (MockGitIndexProjection gitIndexProjection = new MockGitIndexProjection(new[] { "test.txt" }))
            using (WindowsFileSystemVirtualizer virtualizer = new WindowsFileSystemVirtualizer(this.Repo.Context, this.Repo.GitObjects, mockVirtualization, numWorkThreads))
            using (FileSystemCallbacks fileSystemCallbacks = new FileSystemCallbacks(
                this.Repo.Context,
                this.Repo.GitObjects,
                RepoMetadata.Instance,
                new MockBlobSizes(),
                gitIndexProjection,
                backgroundFileSystemTaskRunner: backgroundTaskRunner,
                fileSystemVirtualizer: virtualizer))
            {
                try
                {
                    string error;
                    fileSystemCallbacks.TryStart(out error).ShouldEqual(true);

                    Guid enumerationGuid = Guid.NewGuid();

                    byte[] contentId = FileSystemVirtualizer.ConvertShaToContentId("0123456789012345678901234567890123456789");
                    byte[] placeholderVersion = WindowsFileSystemVirtualizer.PlaceholderVersionId;

                    uint fileLength = 100;
                    MockGVFSGitObjects mockGVFSGitObjects = this.Repo.GitObjects as MockGVFSGitObjects;
                    mockGVFSGitObjects.FileLength = fileLength;

                    MockTracer mockTracker = this.Repo.Context.Tracer as MockTracer;

                    mockVirtualization.WriteFileReturnResult = HResult.FileClosed;
                    mockVirtualization.OnGetFileStream(
                        commandId: 1,
                        relativePath: "test.txt",
                        byteOffset: 0,
                        length: fileLength,
                        streamGuid: Guid.NewGuid(),
                        contentId: contentId,
                        providerId: placeholderVersion,
                        triggeringProcessId: 2,
                        triggeringProcessImageFileName: "UnitTest").ShouldEqual(HResult.Pending);

                    mockVirtualization.WaitForCompletionStatus().ShouldEqual(mockVirtualization.WriteFileReturnResult);
                    mockTracker.RelatedErrorEvents.ShouldBeEmpty();
                }
                finally
                {
                    fileSystemCallbacks.Stop();
                }
            }
        }
    }
}
