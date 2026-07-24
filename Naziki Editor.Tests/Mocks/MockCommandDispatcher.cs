using Naziki_Editor.Core.Abstractions;

namespace Naziki_Editor.Tests.Mocks
{
    /// <summary>
    /// 模拟 ICommandDispatcher，用于测试快捷键系统。
    /// 记录所有注册和执行的命令，支持自定义 CanExecute 行为。
    /// </summary>
    public class MockCommandDispatcher : ICommandDispatcher
    {
        private readonly Dictionary<string, (Action Execute, Func<bool>? CanExecute)> _commands = new();

        /// <summary>记录所有已注册的命令名称。</summary>
        public List<string> RegisteredCommands { get; } = new();

        /// <summary>记录所有已执行的命令名称。</summary>
        public List<string> ExecutedCommands { get; } = new();

        /// <summary>自定义 CanExecute 返回值，null 表示使用默认行为。</summary>
        public Func<string, bool>? CanExecuteOverride { get; set; }

        public void Register(string commandName, Action execute, Func<bool>? canExecute = null)
        {
            if (string.IsNullOrEmpty(commandName))
                throw new ArgumentException("命令名称不能为空", nameof(commandName));
            if (execute == null)
                throw new ArgumentNullException(nameof(execute));

            _commands[commandName] = (execute, canExecute);
            RegisteredCommands.Add(commandName);
        }

        public bool CanExecute(string commandName)
        {
            if (CanExecuteOverride != null)
                return CanExecuteOverride(commandName);

            if (!_commands.TryGetValue(commandName, out var entry))
                return false;
            return entry.CanExecute?.Invoke() ?? true;
        }

        public void Execute(string commandName)
        {
            if (!_commands.TryGetValue(commandName, out var entry))
                throw new InvalidOperationException($"未注册的命令：{commandName}");

            if (!CanExecute(commandName))
                throw new InvalidOperationException($"命令当前不可用：{commandName}");

            ExecutedCommands.Add(commandName);
            entry.Execute();
        }
    }
}