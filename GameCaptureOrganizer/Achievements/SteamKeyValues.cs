using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace GameCaptureOrganizer.Achievements
{
    /// <summary>Um no da arvore de KeyValues binario da Steam.</summary>
    public class SteamKvNode
    {
        public string Name { get; set; }
        public string StringValue { get; set; }
        public long? IntegerValue { get; set; }
        public List<SteamKvNode> Children { get; private set; }

        public SteamKvNode()
        {
            Children = new List<SteamKvNode>();
        }

        public SteamKvNode Child(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            return Children.Find(c => c != null && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// Leitor do KeyValues BINARIO da Valve — o formato do
    /// <c>appcache\stats\UserGameStats_&lt;conta&gt;_&lt;appId&gt;.bin</c>, que a Steam reescreve
    /// no instante em que uma conquista e destravada.
    ///
    /// O formato: um byte de tipo, o nome terminado em zero, e o valor conforme o tipo. 0x00 abre
    /// um bloco, 0x08 fecha.
    ///
    /// Os tetos (profundidade, quantidade de nos, tamanho de string e do arquivo) nao sao
    /// paranoia: o arquivo e lido enquanto a Steam esta ESCREVENDO nele, entao meio arquivo, ou
    /// lixo no meio, e ocorrencia esperada e nao excecao. Sem os tetos, um byte de tipo lido no
    /// meio de uma escrita vira laco infinito ou alocacao gigante dentro do Playnite.
    /// </summary>
    public static class SteamKeyValues
    {
        private const int MaxDepth = 64;
        private const int MaxNodes = 200000;
        private const int MaxStringBytes = 64 * 1024;
        private const long MaxFileBytes = 64L * 1024L * 1024L;

        public static bool TryRead(string path, out SteamKvNode root)
        {
            root = null;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return false;
            }

            try
            {
                // ReadWrite | Delete: a Steam mantem o arquivo aberto. Abrir exclusivo aqui daria
                // "arquivo em uso" justamente no momento que interessa, que e o do unlock.
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                                                   FileShare.ReadWrite | FileShare.Delete, 4096,
                                                   FileOptions.SequentialScan))
                {
                    if (stream.Length <= 0 || stream.Length > MaxFileBytes)
                    {
                        return false;
                    }

                    using (var reader = new BinaryReader(stream, Encoding.UTF8))
                    {
                        var raiz = new SteamKvNode { Name = string.Empty };
                        var contagem = 0;
                        if (!ReadChildren(reader, raiz, 0, ref contagem, false))
                        {
                            return false;
                        }

                        root = raiz;
                        return raiz.Children.Count > 0;
                    }
                }
            }
            catch (Exception)
            {
                root = null;
                return false;
            }
        }

        private static bool ReadChildren(BinaryReader reader, SteamKvNode parent, int depth,
                                         ref int nodeCount, bool requireEnd)
        {
            if (depth > MaxDepth)
            {
                return false;
            }

            while (reader.BaseStream.Position < reader.BaseStream.Length)
            {
                var tipo = reader.ReadByte();
                if (tipo == 0x08 || tipo == 0x0B)
                {
                    return true;
                }

                var nome = ReadCString(reader);
                if (nome == null || ++nodeCount > MaxNodes)
                {
                    return false;
                }

                var no = new SteamKvNode { Name = nome };
                parent.Children.Add(no);

                switch (tipo)
                {
                    case 0x00: // bloco
                        if (!ReadChildren(reader, no, depth + 1, ref nodeCount, true))
                        {
                            return false;
                        }
                        break;

                    case 0x01: // string
                        no.StringValue = ReadCString(reader);
                        if (no.StringValue == null)
                        {
                            return false;
                        }
                        break;

                    case 0x02: // int32
                    case 0x04: // ponteiro, gravado como int32
                    case 0x06: // cor
                        Require(reader, 4);
                        no.IntegerValue = reader.ReadInt32();
                        break;

                    case 0x03: // float
                        Require(reader, 4);
                        reader.ReadSingle();
                        break;

                    case 0x05: // wstring
                        no.StringValue = ReadWideString(reader);
                        if (no.StringValue == null)
                        {
                            return false;
                        }
                        break;

                    case 0x07: // uint64
                    case 0x09: // int64
                        Require(reader, 8);
                        no.IntegerValue = reader.ReadInt64();
                        break;

                    case 0x0A:
                        no.IntegerValue = 0;
                        break;

                    default:
                        // Tipo desconhecido significa que perdemos o passo dentro do arquivo. Parar
                        // e a unica saida honesta: continuar leria nome onde ha numero.
                        return false;
                }
            }

            return !requireEnd;
        }

        private static string ReadCString(BinaryReader reader)
        {
            var bytes = new List<byte>();
            while (reader.BaseStream.Position < reader.BaseStream.Length && bytes.Count <= MaxStringBytes)
            {
                var b = reader.ReadByte();
                if (b == 0)
                {
                    return Encoding.UTF8.GetString(bytes.ToArray());
                }

                bytes.Add(b);
            }

            return null;
        }

        private static string ReadWideString(BinaryReader reader)
        {
            Require(reader, 2);
            var caracteres = reader.ReadUInt16();
            if (caracteres > MaxStringBytes / 2)
            {
                return null;
            }

            var bytes = caracteres * 2;
            Require(reader, bytes);
            return Encoding.Unicode.GetString(reader.ReadBytes(bytes)).TrimEnd('\0');
        }

        private static void Require(BinaryReader reader, long count)
        {
            if (count < 0 || reader.BaseStream.Length - reader.BaseStream.Position < count)
            {
                throw new EndOfStreamException();
            }
        }
    }
}
