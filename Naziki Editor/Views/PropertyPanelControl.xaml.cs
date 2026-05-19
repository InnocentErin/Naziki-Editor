using System;
using System.Windows.Controls;

namespace Naziki_Editor.Views
{
    public partial class PropertyPanelControl : UserControl
    {
        // 🌟 跨房专用发报机：通知统帅部“有可视化属性被敲改啦！”
        public event Action OnDataModified;
        // 🌟 新增：通知大本营用户主动按下了“应用属性”按钮！
        public event Action OnApplyPropertiesRequested;
        // 📡 跨房专用发报机 2：通知大本营“主人想把当前对象存为素材！”
        public event Action<object> OnSaveAsMaterialRequested;

        public PropertyPanelControl()
        {
            InitializeComponent();
        }

        // 🌟 新增：外部调用这个接口来设置属性面板显示的对象
        public void SetSelectedObject(object obj)
        {
            PropertyInspector.Content = obj;
        }

        // ⌨️ 精准雷达捕获：拦截属性面板内的打字变化
        private void OnPropertyValueChanged(object sender, TextChangedEventArgs e)
        {
            // 防呆设计：只有主人的真实键盘焦点的敲击才算，防止加载绑定时误触发
            if (e.OriginalSource is TextBox tb && tb.IsKeyboardFocusWithin)
            {
                OnDataModified?.Invoke();
            }
        }

        // 🌟 新增：应用属性按钮的点击事件处理器
        private void BtnApplyProperties_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            OnApplyPropertiesRequested?.Invoke();
        }


        // 🌟 点击存为素材按钮
        private void BtnSaveAsMaterial_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            // 如果当前面板里确实有选中的对象，就把它作为 payload 发送给大本营
            if (PropertyInspector.Content != null)
            {
                OnSaveAsMaterialRequested?.Invoke(PropertyInspector.Content);
            }
        }
    }
}