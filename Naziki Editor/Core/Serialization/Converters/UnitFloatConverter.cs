using System;
using Newtonsoft.Json;
using Naziki_Editor.Models;

namespace Naziki_Editor.Core.Serialization.Converters
{
    public class UnitFloatConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) => objectType == typeof(UnitFloat);

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var uf = (UnitFloat)value;
            if (uf == null) { writer.WriteNull(); return; }

            // 如果是默认的 World 参考系（或者纯数字），直接输出数字
            if (uf.Unit == ReferenceUnit.World)
                writer.WriteValue(uf.Value);
            else
            {
                // 🌟 输出官方兼容格式：noteX:0.5
                string unitPrefix = uf.Unit switch
                {
                    ReferenceUnit.NoteX => "noteX",
                    ReferenceUnit.NoteY => "noteY",
                    ReferenceUnit.StageX => "stageX",
                    ReferenceUnit.StageY => "stageY",
                    ReferenceUnit.CameraX => "cameraX",
                    ReferenceUnit.CameraY => "cameraY",
                    _ => "world"
                };
                writer.WriteValue($"{unitPrefix}:{uf.Value}");
            }
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return null;

            var uf = new UnitFloat { Value = 0, Unit = ReferenceUnit.World };

            // 兼容读取：万一读到的是纯数字
            if (reader.TokenType == JsonToken.Integer || reader.TokenType == JsonToken.Float)
            {
                uf.Value = Convert.ToSingle(reader.Value);
                return uf;
            }

            // 兼容读取：读到的是带单位的字符串
            if (reader.TokenType == JsonToken.String)
            {
                string raw = reader.Value.ToString().Trim();
                string rawLower = raw.ToLower();

                // 🌟 格式 1：官方格式 "noteX:0.5" (前缀:值)
                if (rawLower.Contains(":"))
                {
                    var parts = raw.Split(':');
                    if (parts.Length == 2)
                    {
                        string prefix = parts[0].Trim().ToLower();
                        if (float.TryParse(parts[1].Trim(), out float val))
                        {
                            uf.Value = val;
                            uf.Unit = prefix switch
                            {
                                "notex" => ReferenceUnit.NoteX,
                                "notey" => ReferenceUnit.NoteY,
                                "stagex" => ReferenceUnit.StageX,
                                "stagey" => ReferenceUnit.StageY,
                                "camerax" => ReferenceUnit.CameraX,
                                "cameray" => ReferenceUnit.CameraY,
                                _ => ReferenceUnit.World
                            };
                            return uf;
                        }
                    }
                }

                // 🌟 格式 2：项目旧格式 "0.5notex" (值+后缀) - 向后兼容
                if (rawLower.EndsWith("notex")) { uf.Unit = ReferenceUnit.NoteX; rawLower = rawLower.Replace("notex", ""); }
                else if (rawLower.EndsWith("notey")) { uf.Unit = ReferenceUnit.NoteY; rawLower = rawLower.Replace("notey", ""); }
                else if (rawLower.EndsWith("stagex")) { uf.Unit = ReferenceUnit.StageX; rawLower = rawLower.Replace("stagex", ""); }
                else if (rawLower.EndsWith("stagey")) { uf.Unit = ReferenceUnit.StageY; rawLower = rawLower.Replace("stagey", ""); }
                else if (rawLower.EndsWith("camerax")) { uf.Unit = ReferenceUnit.CameraX; rawLower = rawLower.Replace("camerax", ""); }
                else if (rawLower.EndsWith("cameray")) { uf.Unit = ReferenceUnit.CameraY; rawLower = rawLower.Replace("cameray", ""); }

                if (float.TryParse(rawLower, out float v)) uf.Value = v;
                return uf;
            }

            return uf;
        }
    }
}