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
                writer.WriteValue($"{uf.Value}{uf.Unit.ToString().ToLower()}");
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

            // 兼容读取：读到的是带单位的字符串（如 "0.5notex"）
            if (reader.TokenType == JsonToken.String)
            {
                string raw = reader.Value.ToString().Trim().ToLower();
                // 简单的正规化截取
                if (raw.EndsWith("notex")) { uf.Unit = ReferenceUnit.NoteX; raw = raw.Replace("notex", ""); }
                else if (raw.EndsWith("notey")) { uf.Unit = ReferenceUnit.NoteY; raw = raw.Replace("notey", ""); }
                else if (raw.EndsWith("stagex")) { uf.Unit = ReferenceUnit.StageX; raw = raw.Replace("stagex", ""); }
                else if (raw.EndsWith("stagey")) { uf.Unit = ReferenceUnit.StageY; raw = raw.Replace("stagey", ""); }
                else if (raw.EndsWith("camerax")) { uf.Unit = ReferenceUnit.CameraX; raw = raw.Replace("camerax", ""); }
                else if (raw.EndsWith("cameray")) { uf.Unit = ReferenceUnit.CameraY; raw = raw.Replace("cameray", ""); }

                if (float.TryParse(raw, out float v)) uf.Value = v;
                return uf;
            }

            return uf;
        }
    }
}