using System.Diagnostics;

namespace SVNMergeCheckerUI
{
    public interface ISvnService
    {
        bool IsSvnAvailable();
        Task<string?> GetSvnUrlAsync(string pathOrUrl);
        Task<string> UpdateDirectoryAsync(string path);
        /// <summary>
        /// Returns the list of revisions already merged from <paramref name="sourceUrl"/> into <paramref name="targetPath"/>
        /// by running: svn mergeinfo --show-revs merged &lt;sourceUrl&gt; &lt;targetPath&gt;
        /// </summary>
        Task<List<int>> GetMergedRevisionsAsync(string sourceUrl, string targetPath);
    }

    public class SvnService : ISvnService
    {
        public bool IsSvnAvailable()
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
                p.WaitForExit(5000);
                return p.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        public async Task<string?> GetSvnUrlAsync(string pathOrUrl)
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
                new[] { "info", "--show-item", "url", pathOrUrl });

            if (exitCode != 0 || string.IsNullOrWhiteSpace(output))
                return null;

            return output.Trim();
        }

        public async Task<string> UpdateDirectoryAsync(string path)
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

            var (output, error, exitCode) = await RunSvnAsync(new[] { "update", path });

            if (exitCode != 0)
                return $"[ERRORE] svn update fallito:\n{error}";

            return $"[OK] svn update completato:\n{output}";
        }

        public async Task<List<int>> GetMergedRevisionsAsync(string sourceUrl, string targetPath)
        {
            var result = new List<int>();
            if (string.IsNullOrWhiteSpace(sourceUrl) || string.IsNullOrWhiteSpace(targetPath))
                return result;

            var (output, _, exitCode) = await RunSvnAsync(
                new[] { "mergeinfo", "--show-revs", "merged", sourceUrl, targetPath });

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

        private static Task<(string output, string error, int exitCode)> RunSvnAsync(string[] args)
        {
            return Task.Run(() =>
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
                var output = p.StandardOutput.ReadToEnd();
                var error = p.StandardError.ReadToEnd();
                p.WaitForExit();
                return (output, error, p.ExitCode);
            });
        }
    }
}
