using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Residue.Editor.Build
{
    /// <summary>
    /// Runs the EditMode suite and writes the outcome to a file, so an agent with no GUI can find out
    /// whether the tests pass.
    /// <para>
    /// <b>This exists because the obvious approach deadlocks the Editor.</b> Driving
    /// <see cref="TestRunnerApi"/> from a <c>Unity_RunCommand</c> snippet puts the
    /// <see cref="ICallbacks"/> instance in a temporary dynamic assembly, which the post-run domain
    /// reload is in the middle of tearing down: every test reports, <c>RunFinished</c> never arrives,
    /// and the Editor has to be killed. CLAUDE.md documents that trap at length. The reporter has to
    /// live in a real assembly, which is what this is.
    /// </para>
    /// <para>
    /// <see cref="InitializeOnLoadAttribute"/> re-registers on every domain load, for the same
    /// reason: a registration made before a reload does not survive it, so a run that reloads midway
    /// would lose its reporter exactly when it had something to say.
    /// </para>
    /// <para>
    /// Results are appended per test rather than written once at the end. That is deliberate — a run
    /// that dies before finishing still leaves a record of how far it got and which test was last,
    /// and it is what caught a failing assertion on <c>main</c> that nobody could see because nobody
    /// could run the suite.
    /// </para>
    /// </summary>
    [InitializeOnLoad]
    public static class TestRunReport
    {
        /// <summary>Under Temp/ so Unity never imports it as an asset and git never sees it.</summary>
        public const string ReportPath = "Temp/oiledup-editmode.txt";

        static TestRunReport()
        {
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.RegisterCallbacks(new Reporter());
        }

        /// <summary>
        /// Start the EditMode suite. Returns immediately; the run takes a few seconds and the report
        /// is written as it goes.
        /// </summary>
        [MenuItem("Residue/Build/Run EditMode Tests", priority = 60)]
        public static void Run()
        {
            File.WriteAllText(ReportPath, "STARTED\n");

            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.Execute(new ExecutionSettings(new Filter { testMode = TestMode.EditMode }));

            Debug.Log($"[TestRunReport] EditMode run started. Results -> {ReportPath}");
        }

        private sealed class Reporter : ICallbacks
        {
            public void RunStarted(ITestAdaptor suite) { }

            public void TestStarted(ITestAdaptor test) { }

            public void TestFinished(ITestResultAdaptor result)
            {
                if (result.Test.IsSuite) return;

                Append(result.TestStatus == TestStatus.Failed
                    ? $"FAIL {result.Test.FullName}\n     {Flatten(result.Message)}\n"
                    : $"{result.TestStatus} {result.Test.Name}\n");
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                Append($"DONE passed={result.PassCount} failed={result.FailCount} " +
                       $"skipped={result.SkipCount} inconclusive={result.InconclusiveCount} " +
                       $"duration={result.Duration:F1}s\n");
            }

            /// <summary>A multi-line NUnit message would look like several test results otherwise.</summary>
            private static string Flatten(string message) =>
                string.IsNullOrEmpty(message)
                    ? "(no message)"
                    : message.Replace("\r", " ").Replace("\n", " ");

            private static void Append(string line)
            {
                try { File.AppendAllText(ReportPath, line); }
                catch (IOException e) { Debug.LogWarning($"[TestRunReport] {e.Message}"); }
            }
        }
    }
}
