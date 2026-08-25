using System.Diagnostics;

namespace SVNMergeCheckerUI
{
    public interface ISvnService
    {
        Task<bool> IsSvnAvailableAsync();
        Task<string?> GetSvnUrlAsync(string pathOrUrl, CancellationToken ct = default, int timeoutMs = 60_000);
        Task<string> UpdateDirectoryAsync(string path, int timeoutMs = 60_000);
        /// <summary>
        /// Returns the list of revisions already merged from <paramref name="sourceUrl"/> into <paramref name="targetPath"/>
        /// by running: svn mergeinfo --show-revs merged &lt;sourceUrl&gt; &lt;targetPath&gt;
        /// </summary>
        Task<List<int>> GetMergedRevisionsAsync(string sourceUrl, string targetPath, CancellationToken ct = default, int timeoutMs = 60_000);
    }

    public class SvnService : ISvnService
    {
        // Timeout di default (ms) per le chiamate a svn.exe che non hanno un timeout esplicito.
        // TODO: rendere configurabile da AppConfig/GUI (vedi TASKS.md).
        private const int DefaultSvnTimeoutMs = 60_000;

        public async Task<bool> IsSvnAvailableAsync()
        {
            try
            {
                using var p = new Process();
                p.StartInfo = new ProcessStartInfo("svn", "--version --quiet")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                p.Start();

                var exitTask = p.WaitForExitAsync();
                var completed = await Task.WhenAny(exitTask, Task.Delay(5000)) == exitTask;
                if (!completed)
                {
                    try { p.Kill(entireProcessTree: true); } catch { }
                    return false;
                }
                return p.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        public async Task<string?> GetSvnUrlAsync(string pathOrUrl, CancellationToken ct = default, int timeoutMs = DefaultSvnTimeoutMs)
        {
            if (string.IsNullOrWhiteSpace(pathOrUrl))
                return null;

            // If it's already an HTTP/HTTPS URL, return it directly
            if (pathOrUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                pathOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                pathOrUrl.StartsWith("svn://", StringComparison.OrdinalIgnoreCase) ||
                pathOrUrl.StartsWith("svn+ssh://", StringComparison.OrdinalIgnoreCase))
            {
                return pathOrUrl.Trim();
            }

            // It's a local path: run svn info to extract the URL
            var (output, _, exitCode) = await RunSvnAsync(
                new[] { "info", "--show-item", "url", pathOrUrl }, ct, timeoutMs);

            if (exitCode != 0 || string.IsNullOrWhiteSpace(output))
                return null;

            return output.Trim();
        }

        public async Task<string> UpdateDirectoryAsync(string path, int timeoutMs = DefaultSvnTimeoutMs)
        {
            if (string.IsNullOrWhiteSpace(path))
                return "[SKIP] Percorso non specificato.";

            if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("svn://", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("svn+ssh://", StringComparison.OrdinalIgnoreCase))
            {
                return $"[SKIP] '{path}' è un URL remoto: svn update non applicabile.";
            }

            if (!Directory.Exists(path))
                return $"[SKIP] Il percorso '{path}' non esiste sul file system.";

            var (output, error, exitCode) = await RunSvnAsync(new[] { "update", path }, CancellationToken.None, timeoutMs);

            if (exitCode != 0)
                return $"[ERRORE] svn update fallito:\n{error}";

            return $"[OK] svn update completato:\n{output}";
        }

        public async Task<List<int>> GetMergedRevisionsAsync(string sourceUrl, string targetPath, CancellationToken ct = default, int timeoutMs = DefaultSvnTimeoutMs)
        {
            var result = new List<int>();
            if (string.IsNullOrWhiteSpace(sourceUrl) || string.IsNullOrWhiteSpace(targetPath))
                return result;

            var (output, _, exitCode) = await RunSvnAsync(
                new[] { "mergeinfo", "--show-revs", "merged", sourceUrl, targetPath }, ct, timeoutMs);

            if (exitCode != 0 || string.IsNullOrWhiteSpace(output))
                return result;

            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = line.Trim().TrimStart('r');
                if (int.TryParse(trimmed, out var rev))
                    result.Add(rev);
            }
            return result;
        }

        private static async Task<(string output, string error, int exitCode)> RunSvnAsync(string[] args, CancellationToken ct, int timeoutMs = DefaultSvnTimeoutMs)
        {
            using var p = new Process();
            p.StartInfo = new ProcessStartInfo("svn", string.Join(" ", args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a)))
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            p.Start();

            var outputTask = p.StandardOutput.ReadToEndAsync(ct);
            var errorTask = p.StandardError.ReadToEndAsync(ct);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeoutMs);

            try
            {
                await p.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                try { p.Kill(entireProcessTree: true); } catch { /* ignored */ }
                throw;
            }
            catch (OperationCanceledException)
            {
                try { p.Kill(entireProcessTree: true); } catch { /* ignored */ }
                throw new TimeoutException($"Il comando 'svn {string.Join(" ", args)}' non ha risposto entro {timeoutMs} ms.");
            }

            var output = await outputTask;
            var error = await errorTask;
            return (output, error, p.ExitCode);
        }
    }
}
