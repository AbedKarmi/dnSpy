using dnSpy.Contracts.MVVM;

namespace dnSpy.AIChat.UI {
	sealed class ChatMessage : ViewModelBase {
		public string Role { get; }

		string content;
		public string Content {
			get => content;
			private set { if (content != value) { content = value; OnPropertyChanged(nameof(Content)); } }
		}

		public ChatMessage(string role, string content) {
			Role = role;
			this.content = content;
		}

		public void Append(string text) => Content += text;
	}
}
