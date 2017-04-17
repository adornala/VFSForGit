﻿using GVFS.FunctionalTests.Category;
using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Properties;
using GVFS.FunctionalTests.Should;
using GVFS.FunctionalTests.Tools;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace GVFS.FunctionalTests.Tests
{
    [TestFixture]
    [Category(CategoryConstants.FastFetch)]
    public class FastFetchTests
    {
        private readonly string fastFetchRepoRoot = Settings.Default.FastFetchRoot;
        private readonly string fastFetchControlRoot = Settings.Default.FastFetchControl;

        [OneTimeSetUp]
        public void InitControlRepo()
        {
            Directory.CreateDirectory(this.fastFetchControlRoot);
            GitProcess.Invoke("C:\\", "clone -b " + Settings.Default.Commitish + " " + Settings.Default.RepoToClone + " " + this.fastFetchControlRoot);
        }

        [SetUp]
        public void InitRepo()
        {
            Directory.CreateDirectory(this.fastFetchRepoRoot);
            GitProcess.Invoke(this.fastFetchRepoRoot, "init");
            GitProcess.Invoke(this.fastFetchRepoRoot, "remote add origin " + Settings.Default.RepoToClone);
        }

        [TearDown]
        public void TearDownTests()
        {
            SystemIORunner.RecursiveDelete(this.fastFetchRepoRoot);
        }

        [OneTimeTearDown]
        public void DeleteControlRepo()
        {
            SystemIORunner.RecursiveDelete(this.fastFetchControlRoot);
        }
        
        [TestCase]
        public void CanFetchIntoEmptyGitRepoAndCheckoutWithGit()
        {
            this.RunFastFetch("-b " + Settings.Default.Commitish);

            // Ensure origin/master has been created
            this.GetRefTreeSha("remotes/origin/" + Settings.Default.Commitish).ShouldNotBeNull();

            ProcessResult checkoutResult = GitProcess.InvokeProcess(this.fastFetchRepoRoot, "checkout " + Settings.Default.Commitish);
            checkoutResult.Errors.ShouldEqual("Switched to a new branch '" + Settings.Default.Commitish + "'\r\n");
            checkoutResult.Output.ShouldEqual("Branch " + Settings.Default.Commitish + " set up to track remote branch " + Settings.Default.Commitish + " from origin.\n");

            // When checking out with git, must manually update shallow.
            ProcessResult updateRefResult = GitProcess.InvokeProcess(this.fastFetchRepoRoot, "update-ref shallow " + Settings.Default.Commitish);
            updateRefResult.ExitCode.ShouldEqual(0);
            updateRefResult.Errors.ShouldBeEmpty();
            updateRefResult.Output.ShouldBeEmpty();

            this.CurrentBranchShouldEqual(Settings.Default.Commitish);

            this.fastFetchRepoRoot.ShouldBeADirectory(FileSystemRunner.DefaultRunner)
                .WithDeepStructure(this.fastFetchControlRoot, skipEmptyDirectories: false);
        }

        [TestCase]
        public void CanFetchAndCheckoutASingleFolderIntoEmptyGitRepo()
        {
            this.RunFastFetch("--checkout --folders \"/GVFS\" -b " + Settings.Default.Commitish);

            this.CurrentBranchShouldEqual(Settings.Default.Commitish);

            this.fastFetchRepoRoot.ShouldBeADirectory(FileSystemRunner.DefaultRunner);
            List<string> dirs = Directory.EnumerateFileSystemEntries(this.fastFetchRepoRoot).ToList();
            dirs.SequenceEqual(new[] 
            {
                Path.Combine(this.fastFetchRepoRoot, ".git"),
                Path.Combine(this.fastFetchRepoRoot, "GVFS"),
                Path.Combine(this.fastFetchRepoRoot, "GVFS.sln")
            });

            Directory.EnumerateFileSystemEntries(Path.Combine(this.fastFetchRepoRoot, "GVFS"), "*", SearchOption.AllDirectories)
                .Count()
                .ShouldEqual(345);

            this.AllFetchedFilePathsShouldPassCheck(path => path.StartsWith("GVFS", StringComparison.OrdinalIgnoreCase));
        }

        [TestCase]
        public void CanFetchAndCheckoutBranchIntoEmptyGitRepo()
        {
            this.RunFastFetch("--checkout -b " + Settings.Default.Commitish);

            this.CurrentBranchShouldEqual(Settings.Default.Commitish);

            this.fastFetchRepoRoot.ShouldBeADirectory(FileSystemRunner.DefaultRunner)
                .WithDeepStructure(this.fastFetchControlRoot, skipEmptyDirectories: false);
        }

        [TestCase]
        public void CanFetchAndCheckoutCommitIntoEmptyGitRepo()
        {
            // Get the commit sha for the branch the control repo is on
            string commitSha = GitProcess.Invoke(this.fastFetchControlRoot, "log -1 --format=%H").Trim();

            this.RunFastFetch("--checkout -c " + commitSha);

            string headFilePath = Path.Combine(this.fastFetchRepoRoot, TestConstants.DotGit.Head);
            File.ReadAllText(headFilePath).Trim().ShouldEqual(commitSha);

            // Ensure no errors are thrown with git log
            GitHelpers.CheckGitCommand(this.fastFetchRepoRoot, "log");

            this.fastFetchRepoRoot.ShouldBeADirectory(FileSystemRunner.DefaultRunner)
                .WithDeepStructure(this.fastFetchControlRoot, skipEmptyDirectories: false);
        }

        [TestCase]
        public void CanFetchAndCheckoutBetweenTwoBranchesIntoEmptyGitRepo()
        {
            this.RunFastFetch("--checkout -b " + Settings.Default.Commitish);
            this.CurrentBranchShouldEqual(Settings.Default.Commitish);
            
            // Switch to master
            this.RunFastFetch("--checkout -b master");
            this.CurrentBranchShouldEqual("master");

            // And back
            this.RunFastFetch("--checkout -b " + Settings.Default.Commitish);
            this.CurrentBranchShouldEqual(Settings.Default.Commitish);
            
            this.fastFetchRepoRoot.ShouldBeADirectory(FileSystemRunner.DefaultRunner)
                .WithDeepStructure(this.fastFetchControlRoot, skipEmptyDirectories: false);
        }

        [TestCase]
        public void CanDetectAlreadyUpToDate()
        {
            this.RunFastFetch("--checkout -b " + Settings.Default.Commitish);
            this.CurrentBranchShouldEqual(Settings.Default.Commitish);
            
            this.RunFastFetch(" -b " + Settings.Default.Commitish).ShouldContain("\"TotalMissingObjects\":0");
            this.RunFastFetch("--checkout -b " + Settings.Default.Commitish).ShouldContain("\"RequiredBlobsCount\":0");

            this.CurrentBranchShouldEqual(Settings.Default.Commitish);
            this.fastFetchRepoRoot.ShouldBeADirectory(FileSystemRunner.DefaultRunner)
                .WithDeepStructure(this.fastFetchControlRoot, skipEmptyDirectories: false);
        }

        [TestCase]
        public void SuccessfullyChecksOutCaseChanges()
        {
            // The delta between these two is the same as the UnitTest "caseChange.txt" data file.
            this.RunFastFetch("--checkout -c b5fd7d23706a18cff3e2b8225588d479f7e51138");
            this.RunFastFetch("--checkout -c fd4ae4312eb504fd40e78d2d4cf349004967a8b4");
            
            GitProcess.Invoke(this.fastFetchControlRoot, "checkout fd4ae4312eb504fd40e78d2d4cf349004967a8b4");

            try
            {
                this.fastFetchRepoRoot.ShouldBeADirectory(FileSystemRunner.DefaultRunner)
                    .WithDeepStructure(this.fastFetchControlRoot, skipEmptyDirectories: false, ignoreCase: true);
            }
            finally
            {
                GitProcess.Invoke(this.fastFetchControlRoot, "checkout " + Settings.Default.Commitish);
            }
        }

        private void AllFetchedFilePathsShouldPassCheck(Func<string, bool> checkPath)
        {
            // Form a cache map of sha => path
            string[] allObjects = GitProcess.Invoke(this.fastFetchRepoRoot, "cat-file --batch-check --batch-all-objects").Split('\n');
            string[] gitlsLines = GitProcess.Invoke(this.fastFetchRepoRoot, "ls-tree -r HEAD").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            Dictionary<string, List<string>> allPaths = new Dictionary<string, List<string>>();
            foreach (string line in gitlsLines)
            {
                string sha = this.GetShaFromLsLine(line);
                string path = this.GetPathFromLsLine(line);

                if (!allPaths.ContainsKey(sha))
                {
                    allPaths.Add(sha, new List<string>());
                }

                allPaths[sha].Add(path);
            }

            foreach (string sha in allObjects.Where(line => line.Contains(" blob ")).Select(line => line.Substring(0, 40)))
            {
                allPaths.ContainsKey(sha).ShouldEqual(true, "Found a blob that wasn't in the tree: " + sha);

                // A single blob should map to multiple files, so if any pass for a single sha, we have to give a pass.
                allPaths[sha].Any(path => checkPath(path))
                    .ShouldEqual(true, "Downloaded extra paths:\r\n" + string.Join("\r\n", allPaths[sha]));
            }
        }

        private void CurrentBranchShouldEqual(string commitish)
        {
            // Ensure remote branch has been created
            this.GetRefTreeSha("remotes/origin/" + commitish).ShouldNotBeNull();

            // And head has been updated to local branch, which are both updated
            this.GetRefTreeSha("HEAD")
                .ShouldNotBeNull()
                .ShouldEqual(this.GetRefTreeSha(commitish));

            // Ensure no errors are thrown with git log
            GitHelpers.CheckGitCommand(this.fastFetchRepoRoot, "log");
        }

        private string GetRefTreeSha(string refName)
        {
            string headInfo = GitProcess.Invoke(this.fastFetchRepoRoot, "cat-file -p " + refName);
            if (string.IsNullOrEmpty(headInfo) || headInfo.EndsWith("missing"))
            {
                return null;
            }

            string[] headInfoLines = headInfo.Split('\n');
            headInfoLines[0].StartsWith("tree").ShouldEqual(true);
            int firstSpace = headInfoLines[0].IndexOf(' ');
            string headTreeSha = headInfoLines[0].Substring(firstSpace + 1);
            headTreeSha.Length.ShouldEqual(40);
            return headTreeSha;
        }

        private string RunFastFetch(string args)
        {
            args = args + " --verbose";

            ProcessStartInfo processInfo = new ProcessStartInfo("fastfetch.exe");
            processInfo.Arguments = args;
            processInfo.WorkingDirectory = this.fastFetchRepoRoot;
            processInfo.UseShellExecute = false;
            processInfo.RedirectStandardOutput = true;
            processInfo.RedirectStandardError = true;
            
            ProcessResult result = ProcessHelper.Run(processInfo);
            result.Output.Contains("Error").ShouldEqual(false, result.Output);
            result.Errors.ShouldBeEmpty(result.Errors);
            result.ExitCode.ShouldEqual(0);
            return result.Output;
        }
        
        private string GetShaFromLsLine(string line)
        {
            string output = line.Substring(line.LastIndexOf('\t') - 40, 40);
            return output;
        }

        private string GetPathFromLsLine(string line)
        {
            int idx = line.LastIndexOf('\t') + 1;
            string output = line.Substring(idx, line.Length - idx);
            return output;
        }
    }
}
