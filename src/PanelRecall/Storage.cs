using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace PanelRecall
{
    [DataContract] internal sealed class Binding
    {
        [DataMember] public string Title = "";
        [DataMember] public string Class = "";
        [DataMember] public string Exe = "";
        public bool Configured { get { return !String.IsNullOrEmpty(Exe); } }
    }
    [DataContract] internal sealed class Settings
    {
        [DataMember] public int Version = 1;
        [DataMember] public Binding[] Slots = { new Binding(), new Binding(), new Binding(), new Binding(), new Binding() };
        internal static Settings Load()
        {
            Settings value = Storage.Read<Settings>("settings.json") ?? new Settings();
            if (value.Version != 1 || value.Slots == null || value.Slots.Length != 5) throw new InvalidDataException("配置版本或槽位数量无效。");
            foreach (Binding b in value.Slots)
                if (b == null || b.Title == null || b.Class == null || b.Exe == null) throw new InvalidDataException("配置内容无效。");
            return value;
        }
    }
    [DataContract] internal sealed class ReturnRecord
    {
        [DataMember] public int Version = 1;
        [DataMember] public int Slot;
        [DataMember] public long Window, Cookie, Started;
        [DataMember] public uint Pid, HostPid;
        [DataMember] public long HostStarted;
        [DataMember] public string Class = "", Exe = "", Title = "";
        [DataMember] public int Left, Top, Width, Height;
    }
    internal static class Storage
    {
        internal static string Folder { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PanelRecall"); } }
        private static string FilePath(string name) { return Path.Combine(Folder, name); }
        // Missing files and serialized null have always meant "no record".
        // Keep that contract visible to the modern compiler without requiring
        // nullable-reference syntax or attributes from .NET Framework.
#if NET8_0_OR_GREATER
        [return: System.Diagnostics.CodeAnalysis.MaybeNull]
#endif
        internal static T Read<T>(string name) where T : class
        {
            string path = FilePath(name);
            if (!File.Exists(path)) return null;
            using (var stream = File.OpenRead(path))
            {
                if (stream.Length > 1024 * 1024) throw new InvalidDataException("配置文件过大。");
                var value = new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
                if (value == null) return null;
                return (T)value;
            }
        }
        internal static void Write<T>(string name, T value)
        {
            Directory.CreateDirectory(Folder);
            string path = FilePath(name), temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value); stream.Flush(true); }
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        internal static void ClearReturnRecord() { string path = FilePath("return.json"); if (File.Exists(path)) File.Delete(path); }
    }
}
