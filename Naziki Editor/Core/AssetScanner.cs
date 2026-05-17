using System.Collections.Generic;
using System.IO;

namespace Naziki_Editor.Core
{
    // 📦 这是一个纯粹的数据盒子，用来装扫描到的结果，没有任何界面元素
    public class AssetBundle
    {
        public List<string> Images { get; set; } = new List<string>();
        public List<string> Videos { get; set; } = new List<string>();
    }

    public static class AssetScanner
    {
        // 🔮 扫描法术：只要给它一个路径，它就还你一个分好类的盒子！
        public static AssetBundle ScanFolder(string folderPath)
        {
            AssetBundle bundle = new AssetBundle();
            if (!Directory.Exists(folderPath)) return bundle;

            string[] allFiles = Directory.GetFiles(folderPath);
            foreach (string file in allFiles)
            {
                string ext = Path.GetExtension(file).ToLower();
                string fileName = Path.GetFileName(file);

                if (ext == ".png" || ext == ".jpg" || ext == ".jpeg")
                    bundle.Images.Add(fileName);
                else if (ext == ".mp4" || ext == ".mov")
                    bundle.Videos.Add(fileName);
            }
            return bundle;
        }
    }
}