using System.Windows.Controls;
using Naziki_Editor.Models;
using Naziki_Editor.State;

namespace Naziki_Editor.Views.PropertyEditor
{
    // 🌟 弃用声明外壳：已被全面合并进 FrameList 统一轨道线，保持空代码以防止编译爆红
    public partial class PropertyEditor_InitialState : UserControl
    {
        public PropertyEditor_InitialState()
        {
            InitializeComponent();
        }

        public void LoadData(IStoryboardEntity editingObj, ProjectDataContext context)
        {
            // 已弃用，逻辑已流放到 FrameList 车间
        }
    }
}