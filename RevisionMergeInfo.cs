using System.Diagnostics;
using System.Text;

namespace SVNMergeCheckerUI
{
    public interface IPowerShellRunnerService
    {
        Task<string> RunAsync(
            string scriptPath,
            string workingCopy,
            string sourceRepository,
            string issues,
            string revisions,
            string skipRevisions,
            int maxNewRevs,
            string outFile,
            IProgress<string> progress,
            CancellationToken cancellationToken = default);
    }

    public class PowerShellRunnerService : IPowerShellRunnerService
    {
        public async Task<string> RunAsync(
            string scriptPath,
            string workingCopy,
            string sourceRepository,
            string issues,
            string revisions,
            string skipRevisions,
            int maxNewRevs,
            string outFile,
            IProgress<string> progress,
            CancellationToken cancellationToken = default)
        {
            var args = BuildArguments(
                scriptPath, workingCopy, sourceRepository,
                issues, revisions, skipRevisions, maxNewRevs, outFile);

            var psi = new ProcessStartInfo("powershell.exe", args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

            var fullOutput = new StringBuilder();

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                {
                    var line = e.Data;
                    fullOutput.AppendLine(line);
                    progress.Report(line);
                }
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                {
                    var line = "[ERR] " + e.Data;
                    fullOutput.AppendLine(line);
                    progress.Report(line);
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* ignored */ }
                throw;
            }

            return fullOutput.ToString();
        }

        private static string BuildArguments(
            string scriptPath,
            string workingCopy,
            string sourceRepository,
            string issues,
            string revisions,
            string skipRevisions,
            int maxNewRevs,
            string outFile)
        {
            var sb = new StringBuilder();
            sb.Append("-ExecutionPolicy Bypass -NoProfile -NonInteractive ");
            sb.Append($"-File \"{scriptPath}\" ");
            sb.Append($"-NonInteractive ");

            if (!string.IsNullOrWhiteSpace(workingCopy))
                sb.Append($"-WorkingCopy \"{workingCopy}\" ");

            if (!string.IsNullOrWhiteSpace(sourceRepository))
                sb.Append($"-SourceRepository \"{sourceRepository}\" ");

            if (!string.IsNullOrWhiteSpace(issues))
            {
                var issueTokens = issues.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var formatted = string.Join(",", issueTokens);
                sb.Append($"-Issues {formatted} ");
            }

            if (!string.IsNullOrWhiteSpace(revisions))
            {
                var revTokens = revisions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var formatted = string.Join(" ", revTokens);
                sb.Append($"-Revisions {formatted} ");
            }

            if (!string.IsNullOrWhiteSpace(skipRevisions))
            {
                var skipTokens = skipRevisions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var formatted = string.Join(" ", skipTokens);
                sb.Append($"-SkipRevisions {formatted} ");
            }

            sb.Append($"-MaxNewRevs {maxNewRevs} ");

            if (!string.IsNullOrWhiteSpace(outFile))
                sb.Append($"-OutFile \"{outFile}\" ");

            return sb.ToString().TrimEnd();
        }
    }
}
