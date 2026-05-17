using Naziki_Editor.Core;
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Naziki_Editor.Views
{
    /// <summary>
    /// AssetListControl.xaml 的交互逻辑
    /// </summary>
    public partial class AssetListControl : UserControl
    {
        public AssetListControl()
        {
            InitializeComponent();
        }
        // 🎨 UI 专职：把扫描到的素材画到左下角
        public void RefreshAssetListUI(AssetBundle bundle)
        {
            AssetTreeView.Items.Clear();

            if (bundle.Images.Count > 0)
            {
                TreeViewItem imgFolder = new TreeViewItem() { Header = "图片素材 (Images)" };
                foreach (string img in bundle.Images) imgFolder.Items.Add(new TreeViewItem() { Header = img });
                AssetTreeView.Items.Add(imgFolder);
            }

            if (bundle.Videos.Count > 0)
            {
                TreeViewItem vidFolder = new TreeViewItem() { Header = "视频素材 (Videos)" };
                foreach (string vid in bundle.Videos) vidFolder.Items.Add(new TreeViewItem() { Header = vid });
                AssetTreeView.Items.Add(vidFolder);
            }
        }

        private void ClearAllDrawers()
        {
            AssetTreeView.Items.Clear();
        }




    }
}
