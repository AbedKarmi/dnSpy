using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using dnSpy.AIChat.UI;

namespace dnSpy.AIChat.Services {
	/// <summary>
	/// Talks to the locally installed Claude Code CLI ("claude") in non-interactive mode.
	/// Lets the user use their Claude Pro/Max subscription without an API key.
	/// </summary>
	sealed class ClaudeCliProvider : IChatProvider {
		public async Task SendAsync(IReadOnlyList<ChatMessage> history, string model, Action<string> onChunk, CancellationToken cancellationToken) {
			if (history.Count == 0)
				return;

			var settings = ChatSettings.Load();
			var exe = ResolveExe(settings.ClaudeCliPath);
			if (exe is null)
				throw new InvalidOperationException("Could not find the 'claude' CLI on PATH. Install Claude Code or set ClaudeCliPath in AIChat.json.");

			var prompt = BuildPrompt(history);

			var psi = new ProcessStartInfo {
				FileName = exe,
				Arguments = "-p " + QuoteArg(prompt),
				RedirectStandardInput = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
				CreateNoWindow = true,
				StandardOutputEncoding = Encoding.UTF8,
				StandardErrorEncoding = Encoding.UTF8,
			};

			using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
			if (!proc.Start())
				throw new InvalidOperationException("Failed to start claude CLI.");

			var stderrTask = Task.Run(() => proc.StandardError.ReadToEndAsync());

			var buffer = new char[1024];
			while (true) {
				cancellationToken.ThrowIfCancellationRequested();
				int read = await proc.StandardOutput.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
				if (read <= 0)
					break;
				onChunk(new string(buffer, 0, read));
			}

#if NET5_0_OR_GREATER
			await proc.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
#else
			await Task.Run(() => proc.WaitForExit(), cancellationToken).ConfigureAwait(false);
#endif

			if (proc.ExitCode != 0) {
				var err = await stderrTask.ConfigureAwait(false);
				throw new InvalidOperationException($"claude CLI exited with code {proc.ExitCode}. {err}");
			}
		}

		static string BuildPrompt(IReadOnlyList<ChatMessage> history) {
			// Claude CLI -p takes a single prompt; encode the conversation as plain text
			if (history.Count == 1)
				return history[0].Content;
			var sb = new StringBuilder();
			foreach (var m in history) {
				sb.Append(m.Role == "user" ? "User: " : "Assistant: ");
				sb.AppendLine(m.Content);
				sb.AppendLine();
			}
			sb.Append("Assistant:");
			return sb.ToString();
		}

		static string? ResolveExe(string? configured) {
			if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
				return configured;

			foreach (var name in new[] { "claude.cmd", "claude.exe", "claude" }) {
				var found = SearchPath(name);
				if (found != null)
					return found;
			}
			return null;
		}

		static string? SearchPath(string fileName) {
			var pathEnv = Environment.GetEnvironmentVariable("PATH");
			if (string.IsNullOrEmpty(pathEnv))
				return null;
			foreach (var dir in pathEnv.Split(Path.PathSeparator)) {
				try {
					var full = Path.Combine(dir, fileName);
					if (File.Exists(full))
						return full;
				}
				catch { }
			}
			return null;
		}

		// Quote a single Windows process argument per the standard CRT rules.
		static string QuoteArg(string s) {
			if (s.Length > 0 && s.IndexOfAny(new[] { ' ', '\t', '\n', '\v', '"' }) < 0)
				return s;
			var sb = new StringBuilder();
			sb.Append('"');
			int backslashes = 0;
			foreach (var ch in s) {
				if (ch == '\\') { backslashes++; continue; }
				if (ch == '"') {
					sb.Append('\\', backslashes * 2 + 1);
					sb.Append('"');
				}
				else {
					sb.Append('\\', backslashes);
					sb.Append(ch);
				}
				backslashes = 0;
			}
			sb.Append('\\', backslashes * 2);
			sb.Append('"');
			return sb.ToString();
		}
	}
}
