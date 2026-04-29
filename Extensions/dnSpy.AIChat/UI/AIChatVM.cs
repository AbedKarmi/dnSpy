using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using dnSpy.AIChat.Services;
using dnSpy.Contracts.MVVM;

namespace dnSpy.AIChat.UI {
	sealed class AIChatVM : ViewModelBase, IDisposable {
		public ObservableCollection<ChatMessage> Messages { get; } = new();
		public string[] Providers { get; } = new[] { "Claude CLI (subscription)", "Anthropic API", "OpenAI API" };

		string selectedProvider = "Claude CLI (subscription)";
		public string SelectedProvider {
			get => selectedProvider;
			set {
				if (selectedProvider != value) {
					selectedProvider = value;
					OnPropertyChanged(nameof(SelectedProvider));
					var settings = ChatSettings.Load();
					settings.Provider = value;
					settings.Save();
					Model = ChatSettings.DefaultModelFor(value);
				}
			}
		}

		string model = "";
		public string Model {
			get => model;
			set {
				if (model != value) {
					model = value ?? "";
					OnPropertyChanged(nameof(Model));
					var settings = ChatSettings.Load();
					settings.Model = model;
					settings.Save();
				}
			}
		}

		string promptText = "";
		public string PromptText {
			get => promptText;
			set {
				if (promptText != value) {
					promptText = value ?? "";
					OnPropertyChanged(nameof(PromptText));
					CommandManager.InvalidateRequerySuggested();
				}
			}
		}

		string status = "Ready";
		public string Status {
			get => status;
			set { if (status != value) { status = value; OnPropertyChanged(nameof(Status)); } }
		}

		bool includeSelection;
		public bool IncludeSelection {
			get => includeSelection;
			set { if (includeSelection != value) { includeSelection = value; OnPropertyChanged(nameof(IncludeSelection)); } }
		}

		public RelayCommand SendCommand { get; }
		public RelayCommand CancelCommand { get; }
		public RelayCommand ClearCommand { get; }
		public RelayCommand OpenSettingsCommand { get; }

		CancellationTokenSource? cts;
		bool isSending;

		public AIChatVM() {
			var s = ChatSettings.Load();
			var provider = string.IsNullOrEmpty(s.Provider) ? Providers[0] : s.Provider!;
			selectedProvider = provider;
			model = string.IsNullOrEmpty(s.Model) ? ChatSettings.DefaultModelFor(provider) : s.Model!;

			SendCommand = new RelayCommand(_ => _ = SendAsync(), _ => !isSending && !string.IsNullOrWhiteSpace(PromptText));
			CancelCommand = new RelayCommand(_ => cts?.Cancel(), _ => isSending);
			ClearCommand = new RelayCommand(_ => Messages.Clear());
			OpenSettingsCommand = new RelayCommand(_ => SettingsDialog.Show());
		}

		static void RequeryCommands() => CommandManager.InvalidateRequerySuggested();

		async Task SendAsync() {
			var prompt = PromptText.Trim();
			if (prompt.Length == 0)
				return;

			if (IncludeSelection) {
				var sel = SelectionContextProvider.TryGetSelection();
				if (!string.IsNullOrEmpty(sel))
					prompt = prompt + "\n\n--- dnSpy current selection ---\n" + sel;
			}

			Messages.Add(new ChatMessage("user", prompt));
			PromptText = "";
			isSending = true;
			Status = "Sending…";
			CommandManager.InvalidateRequerySuggested();

			var assistant = new ChatMessage("assistant", "");
			Messages.Add(assistant);

			cts = new CancellationTokenSource();
			try {
				IChatProvider provider = ChatProviderFactory.Create(SelectedProvider);
				var history = new System.Collections.Generic.List<ChatMessage>(Messages);
				history.RemoveAt(history.Count - 1); // remove the placeholder

				await provider.SendAsync(history, Model, chunk => {
					Application.Current?.Dispatcher.Invoke(() => assistant.Append(chunk));
				}, cts.Token).ConfigureAwait(false);
				Status = "Ready";
			}
			catch (OperationCanceledException) {
				assistant.Append("\n[cancelled]");
				Status = "Cancelled";
			}
			catch (Exception ex) {
				assistant.Append("\n[error] " + ex.Message);
				Status = "Error";
			}
			finally {
				isSending = false;
				cts?.Dispose();
				cts = null;
				Application.Current?.Dispatcher.Invoke(() => {
					CommandManager.InvalidateRequerySuggested();
				});
			}
		}

		public void Dispose() {
			cts?.Cancel();
			cts?.Dispose();
		}
	}
}
