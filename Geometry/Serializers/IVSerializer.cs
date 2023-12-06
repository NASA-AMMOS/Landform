using System;
using System.IO;
using System.Net;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;

namespace JPLOPS.Geometry
{
    /// <summary>
    /// This parser does not fully implement the Open Inventor format.
    /// It only supports the minimal feature set to read IDS mesh products.
    ///
    /// OpenInventor files can either be in ASCII or binary format.  Neither is very well documented, but there appears
    /// to be almost no documentation for binary format other than what we put together below.
    ///
    /// Binary format can be converted to ASCII (and vice-versa) using ivcat.exe from inventor-tools
    ///
    /// https://sourceforge.net/projects/inventor-tools/
    ///
    /// which uses Coin3D, an open source cleanroom clone of Inventor
    ///   
    /// https://bitbucket.org/Coin3D/coin
    ///
    /// Our legacy implementation converted binary files to ASCII with ivcat.exe and then parses the ASCII.
    /// This is problematic for a few reasons.
    ///
    /// ---
    ///
    /// Notes on based on reverse engineering Coin3D sources as well as inspection of sample files.
    ///
    /// ASCII format starts with a line like
    ///
    /// #Inventor V2.1 ascii
    ///
    /// and uses Unix style line endings (newline '\n'=0x0a)
    ///   
    /// binary format starts with a line like
    ///   
    /// #Inventor V2.1 binary
    ///
    /// (likely padded at the end with spaces) and terminated with a newline character '\n'=0x0a
    /// the padding appears to be intended to make the string, including the terminator, take up a multiple of 4 bytes
    ///
    /// In both the ASCII and binary formats the rest of the file is structured as a tree of Nodes.
    /// Nodes may have data Fields and/or children Nodes.
    /// The semantics of each Field are determined by the Field's name and the enclosing Node's type.
    ///
    /// Unfortunately in both ASCII and binary formats the size in bytes of any Node or Field is not easily knowable
    /// without parsing it entirely, and requires knowledge of the semantics of that particular type of Node or Field.
    /// Since we only implement the types required for parsing a typical IDS wedge mesh, this means that our parsers are
    /// based on scanning for recognized Node and Field names.  This is potentially prone to false positives, as it's
    /// possible that an expected name could also appear in a comment, quoted string (neither of which we properly
    /// detect), or other binary data.  We compensate for this possibility in part by cross-checking the data for self
    /// consistency.
    ///
    /// In ASCII:
    /// * tokens are separated by whitespace
    /// * a Node is either a RegularNode, a DefNode, or a UseNode
    /// * a RegularNode is NodeType { FieldList ChildList }
    /// * NodeType is a string giving the node type, typically in StudlyCaps
    /// * FieldList is a list of zero or more Fields (the possible set of which are determined by the node type)
    /// * ChildList is a list of zero or more Nodes (only present if the node type is Group or Separator)
    /// * a Field is FieldName FieldValue
    /// * FieldName is typically in camelCase
    /// * FieldValue may be a string, a number, or a Vector of whitespace separated strings or numbers
    /// * FieldValue may be a comma separated list in square brackets of strings, numbers, or Vectors
    /// * a DefNode is DEF Name RegularNode
    /// * a UseNode is USE Name
    ///
    /// In binary:
    /// * ints are 4 bytes in network byte order
    /// * floats are 4 IEE754 bytes in network byte order
    /// * strings are StringLength StringChars StringPadding
    /// * StringLength is an int giving the number NC of StringChars
    /// * StringChars are NC ASCII characters
    /// * StringPadding is P bytes of zero padding such that NC + P is a multiple of 4
    /// * a Node is either a RegularNode, a DefNode, or a UseNode
    /// * a RegularNode is NodeType NodeFlags FieldListFlags FieldList ChildList
    /// * NodeType is a string giving the node type, typically in StudlyCaps
    /// * NodeFlags is an int TODO
    /// * FieldListFlags is an int where the low byte is the number of fields NF and the second byte is TODO
    /// * FieldList is a list of NF Fields
    /// * a Field is FieldName FieldValue FieldFlags
    /// * FieldName is a string typically in camelCase
    /// * FieldValue may be an int, float, string, or a Vector of ints, floats, or strings
    /// * FieldValue may be an Array of ints, floats, strings, or Vectors of ints, floats, or strings
    /// * an Array is ArrayLength ArrayValues where ArrayLength is an int giving the number of values in the array
    /// * FieldFlags is an int TODO
    /// * ChildList is an int NC and then NC Nodes (only present if the node type is Group or Separator)
    /// * a DefNode is DEF Name RegularNode where DEF is the string "DEF" and Name is a string
    /// * a UseNode is USE Name where USE is the string "USE" and Name is a string
    /// 
    /// Typical structure of IDS wedge mesh (multiple LevelOfDetail nodes are treated as patches to be merged):
    /// 
    /// Header
    /// Separator {
    ///   Separator {
    ///     Group {
    ///       DEF Texture_PRODUCTID Texture2 {
    ///         filename "PRODUCTID.rgb"
    ///         wrapS CLAMP
    ///         wrapT CLAMP
    ///       }
    ///       ShapeHints {
    ///         vertexOrdering COUNTERCLOCKWISE
    ///         shapeType SOLID
    ///       }
    ///       TextureCoordinateBinding {
    ///         value PER_VERTEX
    ///       }
    ///       LevelOfDetail {
    ///         screenArea [ A0, A1, ... ]
    ///         Separator {
    ///           Coordinate3 {
    ///             point [ X0 Y0 Z0, X1 Y1 Z1, ... ]
    ///           }
    ///           DEF _PRODUCTID Separator {
    ///             USE Texture_PRODUCTID
    ///             TextureCoordinate2 {
    ///               point [ U0 V0, U1 V1, ... ]
    ///             }
    ///             IndexedTriangleStripSet {
    ///               coordIndex [ I0, I1, I2, ... ]
    ///             }
    ///           }
    ///         }
    ///         Separator {
    ///           Coordinate3 {
    ///             point [ X0 Y0 Z0, X1 Y1 Z1, ... ]
    ///           }
    ///           DEF _PRODUCTID Separator {
    ///             USE Texture_PRODUCTID
    ///             TextureCoordinate2 {
    ///               point [ U0 V0, U1 V1, ... ]
    ///             }
    ///             IndexedTriangleStripSet {
    ///               coordIndex [ I0, I1, I2, ... ]
    ///             }
    ///           }
    ///         }
    ///         ...
    ///       }
    ///     }
    ///   }
    /// }
    /// </summary>
    public class IVSerializer : MeshSerializer
    {
        private enum NodeType { None, Texture2, ShapeHints, TextureCoordinateBinding, LevelOfDetail,
                                Coordinate3, TextureCoordinate2, IndexedTriangleStripSet };
        private enum FieldName { None, filename, wrapS, wrapT,
                                 vertexOrdering, shapeType, value, screenArea, point, coordIndex };

        private const int BIN_FILE_BUF_SIZE = 65535;

        private string textureFile;

        /// <summary>
        /// Returns all LOD meshes in order from highest quality to lowest quality.
        /// </summary>
        public override List<Mesh> LoadAllLODs(string filename, out string imageFilename,
                                               bool onlyGetImageFilename = false)
        {
            List<Mesh> lodMeshes = null;
            lodMeshes = Parse(filename, onlyGetImageFilename);
            imageFilename = textureFile;
            return lodMeshes;
        }

        public override List<Mesh> LoadAllLODs(string filename)
        {
            return LoadAllLODs(filename, out string imageFilename);
        }

        public override bool SupportsLODs()
        {
            return true;
        }

        public override Mesh Load(string filename, out string imageFilename, bool onlyGetImageFilename = false)
        {
            var lodMeshes = LoadAllLODs(filename, out imageFilename, onlyGetImageFilename);
            return onlyGetImageFilename ? null : lodMeshes[0];
        }

        public override Mesh Load(string filename)
        {
            return Load(filename, out string imageFilename);
        }

        public override void Save(Mesh m, string filename, string imageFilename)
        {
            throw new NotImplementedException();
        }

        public override string GetExtension()
        {
            return ".iv";
        }
        
        private void CheckHeader(string header, out bool isBinary)
        {
            if (string.IsNullOrEmpty(header))
            {
                throw new IOException("end of file while reading header");
            }
            header = header.Trim().ToLower();
            if (!header.StartsWith("#inventor v2.1") || !(header.EndsWith("ascii") || header.EndsWith("binary")))
            {
                throw new Exception(string.Format("unrecognized header \"{0}\", " +
                                                  "expected \"#Inventor V2.1 ascii|binary\"", header));
            }
            isBinary = header.EndsWith("binary");
        }

        private void CheckHeader(Stream fileStream, out bool isBinary)
        {
            isBinary = false;
            int curByte;
            var header = new StringBuilder();
            while ((curByte = fileStream.ReadByte()) != -1)
            {
                if (curByte == '\n')
                {
                    CheckHeader(header.ToString(), out isBinary);
                    return;
                }
                header.Append((char)curByte);
                if (header.Length > 100)
                {
                    throw new Exception("error parsing header");
                }
            }
            if (curByte == -1)
            {
                throw new Exception("unexpected end of file while parsing header");
            }
        }

        private FileStream OpenFile(string filename)
        {
            return new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read);
        }

        private List<Mesh> Parse(string filename, bool onlyGetImageFilename = false)
        {
            bool isBinary = false;
            using (var fs = OpenFile(filename))
            {
                CheckHeader(fs, out isBinary);
            }
            if (isBinary)
            {
                return ParseBinary(filename, onlyGetImageFilename);
            }
            else
            {
                return ParseASCII(filename, onlyGetImageFilename);
            }
        }

        private List<Mesh> ParseASCII(string filename, bool onlyGetImageFilename = false)
        {
            using (var fileReader = new StreamReader(filename))
            {
                CheckHeader(fileReader.ReadLine(), out bool isBinary);
                if (isBinary)
                {
                    throw new Exception("binary format detected");
                }

                IEnumerator<string> getTokenizer()
                {
                    char[] separators = " \t\n\r{},".ToCharArray();
                    string line;
                    while ((line = fileReader.ReadLine()) != null)
                    {
                        //NOTE: this tokenizer does not properly implement parsing of quoted strings
                        //currently the only place we expect a quoted string is in the value of the filename field
                        //in a Texture2 node, and there we assume
                        //(a) no whitespace in the string (including no multiline strings)
                        //(b) no escape sequences in the string
                        //(c) no # characters in the string
                        int commentStart = line.IndexOf('#');
                        if (commentStart == 0)
                        {
                            continue;
                        }
                        if (commentStart > 0)
                        {
                            line = line.Substring(0, commentStart);
                        }
                        foreach (string token in line.Split(separators))
                        {
                            if (!string.IsNullOrEmpty(token))
                            {
                                yield return token;
                            }
                        }
                    }
                    yield break;
                }

                var tokenizer = getTokenizer();
                var curNode = NodeType.None;
                var curField = FieldName.None;

                var nodeTypes = new Dictionary<string, NodeType>();
                foreach (var nodeType in (NodeType[])Enum.GetValues(typeof(NodeType)))
                {
                    if (nodeType != NodeType.None)
                    {
                        nodeTypes[nodeType.ToString()] = nodeType;
                    }
                }

                var fieldNames = new Dictionary<string, FieldName>();
                foreach (var fieldName in (FieldName[])Enum.GetValues(typeof(FieldName)))
                {
                    if (fieldName != FieldName.None)
                    {
                        fieldNames[fieldName.ToString()] = fieldName;
                    }
                }

                bool parseNodeType()
                {
                    if (!nodeTypes.ContainsKey(tokenizer.Current)) //Enum.TryParse() also recognizes ints
                    {
                        return false;
                    }
                    curNode = nodeTypes[tokenizer.Current];
                    curField = FieldName.None;
                    return true;
                }

                bool parseFieldName()
                {
                    if (!fieldNames.ContainsKey(tokenizer.Current)) //Enum.TryParse() also recognizes ints
                    {
                        return false;
                    }
                    curField = fieldNames[tokenizer.Current];
                    return true;
                }

                string checkField(FieldName expectedField, string expectedValue)
                {
                    if (parseFieldName() && curField == expectedField)
                    {
                        string value = tokenizer.MoveNext() ? tokenizer.Current : null;
                        if (expectedValue != null  && value != expectedValue)
                        {
                            value = value ?? "(end of file)";
                            throw new Exception(string.Format("expected {0}.{1} = {2}, got {3}",
                                                              curNode, curField, expectedValue, value));
                        }
                        return value;
                    }
                    return null;
                }
                
                void parseList<T>(List<T> list, Func<string, string, T> parseElement, string what)
                {
                    list.Clear();
                    bool started = false, ended = false;
                    while (!ended && tokenizer.MoveNext()) //order important: don't eat token if ended
                    {
                        string token = tokenizer.Current;
                        if (token.StartsWith("[")) //tolerate no space after [
                        {
                            if (started)
                            {
                                throw new Exception("unexpected token \"[\" while reading " + what);
                            }
                            started = true;
                        }
                        ended = started && token.EndsWith("]"); //tolerate no space before ]
                        token = token.Trim("[]".ToCharArray());
                        if (started && token != "")
                        {
                            list.Add(parseElement(token, what));
                        }
                    }
                    if (!ended)
                    {
                        throw new Exception("end of file while reading " + what);
                    }
                }

                Vector2 parseV2(string x, string what)
                {
                    if (!tokenizer.MoveNext())
                    {
                        throw new IOException("end of file while reading " + what);
                    }
                    return new Vector2(double.Parse(x), double.Parse(tokenizer.Current));
                }

                Vector3 parseV3(string x, string what)
                {
                    var v = new Vector3(double.Parse(x), 0, 0);
                    for (int i = 0; i < 2; i++)
                    {
                        if (!tokenizer.MoveNext())
                        {
                            throw new IOException("end of file while reading " + what);
                        }
                        v[i + 1] = double.Parse(tokenizer.Current);
                    }
                    return v;
                }

                List<double> screenArea = null;
                while (screenArea == null && tokenizer.MoveNext()) //order important: don't eat token if got screenArea
                {
                    if (parseNodeType())
                    {
                        continue;
                    }
                    switch (curNode)
                    {
                        case NodeType.Texture2:
                        {
                            string val = checkField(FieldName.filename, null);
                            if (val != null)
                            {
                                textureFile = val.Trim('"');
                                if (onlyGetImageFilename)
                                {
                                    return null;
                                }
                            }
                            checkField(FieldName.wrapS, "CLAMP");
                            checkField(FieldName.wrapT, "CLAMP");
                            break;
                        }
                        case NodeType.ShapeHints:
                        {
                            checkField(FieldName.vertexOrdering, "COUNTERCLOCKWISE");
                            checkField(FieldName.shapeType, "SOLID");
                            break;
                        }
                        case NodeType.TextureCoordinateBinding:
                        {
                            checkField(FieldName.value, "PER_VERTEX");
                            break;
                        }
                        case NodeType.LevelOfDetail:
                        {
                            if (parseFieldName() && curField == FieldName.screenArea)
                            {
                                screenArea = new List<double>();
                                parseList<double>(screenArea, (s, _) => double.Parse(s), "LevelOfDetail.screenArea");
                            }
                            break;
                        }
                        default: break;
                    }
                }
                if (screenArea == null)
                {
                    throw new IOException("end of file while searching for LevelOfDetail");
                }

                var lodMeshes = new List<List<Mesh>>();
                var vertices = new List<Vector3>();
                var texCoords = new List<Vector2>();
                var triStripIndices = new List<int>();

                curNode = NodeType.None;
                curField = FieldName.None;
                int lod = 0, patch = 0;

                while (tokenizer.MoveNext())
                {
                    parseNodeType();
                    parseFieldName();

                    if (curNode == NodeType.LevelOfDetail)
                    {
                        //Console.WriteLine("read {0} LODs in patch {1}", lod, patch);
                        lod = 0;
                        patch++;
                        curNode = NodeType.None;
                        curField = FieldName.None;
                    }
                    else if (curNode == NodeType.Coordinate3 && curField == FieldName.point)
                    {
                        parseList<Vector3>(vertices, parseV3, "Coordinate3.point");
                        curField = FieldName.None;
                    }
                    else if (curNode == NodeType.TextureCoordinate2 && curField == FieldName.point)
                    {
                        parseList<Vector2>(texCoords, parseV2, "TextureCoordinate2.point");
                        curField = FieldName.None;
                    }
                    else if (curNode == NodeType.IndexedTriangleStripSet && curField == FieldName.coordIndex)
                    {
                        parseList<int>(triStripIndices, (s, _) => int.Parse(s), "IndexedTriangleStripSet.coordIndex");
                        AddLODMesh(ref lod, lodMeshes, vertices, texCoords, triStripIndices, filename);
                        curField = FieldName.None;
                    }
                }
                //Console.WriteLine("read {0} LODs in patch {1}", lod, patch);

                return MergeLODMeshes(lodMeshes, screenArea.Count);
            }
        }

        private List<Mesh> ParseBinary(string filename, bool onlyGetImageFilename = false)
        {
            using (var fileStream = new BufferedStream(OpenFile(filename), BIN_FILE_BUF_SIZE))
            {
                CheckHeader(fileStream, out bool isBinary);
                if (!isBinary)
                {
                    throw new Exception("ASCII format detected");
                }
                
                int curByte = -1;
                var curNode = NodeType.None;
                var curField = FieldName.None;

                var lookaheadQueue = new Queue<byte>();

                NodeType[] idNodeType =
                    Enum.GetValues(typeof(NodeType)).Cast<NodeType>().Where(t => t != NodeType.None).ToArray();
                string[] idNodeTypeStr = idNodeType.Select(t => t.ToString()).ToArray();
                bool[] idNodeTypeMatches = new bool[idNodeType.Length];

                FieldName[] idFieldName =
                    Enum.GetValues(typeof(FieldName)).Cast<FieldName>().Where(n => n != FieldName.None).ToArray();
                string[] idFieldNameStr = idFieldName.Select(n => n.ToString()).ToArray();
                bool[] idFieldNameMatches = new bool[idFieldName.Length];

                int idMinLen = Math.Min(idNodeTypeStr.Min(t => t.Length), idFieldNameStr.Min(n => n.Length));
                int idMaxLen = Math.Max(idNodeTypeStr.Max(t => t.Length), idFieldNameStr.Max(n => n.Length));

                bool fillQueue(int n)
                {
                    while (lookaheadQueue.Count < n)
                    {
                        curByte = fileStream.ReadByte();
                        if (curByte < 0)
                        {
                            return false;
                        }
                        lookaheadQueue.Enqueue((byte)curByte);
                    }
                    return true;
                }

                void skipBytes(int n, string what = null)
                {
                    for (int i = 0; i < n; i++)
                    {
                        if (lookaheadQueue.Count > 0)
                        {
                            lookaheadQueue.Dequeue();
                        }
                        else if (fileStream.ReadByte() < 0)
                        {
                            throw new IOException("end of file" + (what != null ? (" while reading " + what) : ""));
                        }
                    }
                } 

                byte[] intBytes = new byte[4];
                int readInt(string what = null, bool skip = true)
                {
                    if (!fillQueue(4))
                    {
                        throw new IOException("end of file" + (what != null ? (" while reading " + what) : ""));
                    }
                    if (skip)
                    {
                        for (int i = 0; i < 4; i++)
                        {
                            intBytes[i] = lookaheadQueue.Dequeue();
                        }
                    }
                    else using (var enumerator = lookaheadQueue.GetEnumerator())
                    {
                        for (int i = 0; i < 4; i++)
                        {
                            enumerator.MoveNext();
                            intBytes[i] = enumerator.Current;
                        }
                    }
                    return IPAddress.NetworkToHostOrder(BitConverter.ToInt32(intBytes, 0));
                }

                int stringPadding(int n)
                {
                    switch (n % 4)
                    {
                        case 1: return 3;
                        case 2: return 2;
                        case 3: return 1;
                        default: return 0;
                    }
                }

                bool readNextIdentifier()
                {
                    while (fillQueue(4))
                    {
                        int len = readInt("identifier length", skip: false);
                        if (len >= idMinLen && len <= idMaxLen)
                        {
                            for (int i = 0; i < idNodeTypeMatches.Length; i++)
                            {
                                idNodeTypeMatches[i] = len == idNodeTypeStr[i].Length;
                            }
                            for (int i = 0; i < idFieldNameMatches.Length; i++)
                            {
                                idFieldNameMatches[i] = len == idFieldNameStr[i].Length;
                            }
                            if ((idNodeTypeMatches.Any(m => m) || idFieldNameMatches.Any(m => m)) && fillQueue(4 + len))
                            {
                                using (var enumerator = lookaheadQueue.GetEnumerator())
                                {
                                    for (int i = 0; i < 4; i++) //skip over string length
                                    {
                                        enumerator.MoveNext();
                                    }
                                    for (int i = 0; i < len; i++)
                                    {
                                        enumerator.MoveNext();
                                        char curChar = (char)enumerator.Current;
                                        for (int j = 0; j < idNodeTypeMatches.Length; j++)
                                        {
                                            if (idNodeTypeMatches[j])
                                            {
                                                idNodeTypeMatches[j] = curChar == idNodeTypeStr[j][i];
                                                if (i == (len - 1) && idNodeTypeMatches[j])
                                                {
                                                    skipBytes(4 + len, idNodeTypeStr[j]);
                                                    skipBytes(stringPadding(len), idNodeTypeStr[j] + " padding");
                                                    skipBytes(4, idNodeTypeStr[j] + ".nodeFlags");
                                                    skipBytes(4, idNodeTypeStr[j] + ".fieldListFlags");
                                                    curNode = idNodeType[j];
                                                    curField = FieldName.None;
                                                    return true;
                                                }
                                            }
                                        }
                                        for (int j = 0; j < idFieldNameMatches.Length; j++)
                                        {
                                            if (idFieldNameMatches[j])
                                            {
                                                idFieldNameMatches[j] = curChar == idFieldNameStr[j][i];
                                                if (i == (len - 1) && idFieldNameMatches[j])
                                                {
                                                    skipBytes(4 + len, idFieldNameStr[j]);
                                                    skipBytes(stringPadding(len), idFieldNameStr[j] + " padding");
                                                    curField = idFieldName[j];
                                                    return true;
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        skipBytes(1); //skip over one byte in input file and try again
                    }
                    return false;
                }

                void parseList<T>(List<T> list, int quadsPerElement, Func<int[], T> parseElement, string what)
                {
                    int len = readInt(what + " length");
                    if (len < 0)
                    {
                        throw new IOException(string.Format("array size {0} while reading {1}", len, what));
                    }
                    list.Clear();
                    list.Capacity = len;
                    var quads = new int[quadsPerElement];
                    for (int i = 0; i < len; i++)
                    {
                        for (int j = 0; j < quadsPerElement; j++)
                        {
                            quads[j] = readInt(string.Format("{0}[{1}].{2}", what, i, j));
                        }
                        list.Add(parseElement(quads));
                    }
                    skipBytes(4, what + ".fieldFlags");
                }

                float parseFloat(int quad)
                {
                    return BitConverter.ToSingle(BitConverter.GetBytes(quad), 0);
                }

                Vector2 parseV2(int[] quads)
                {
                    return new Vector2(parseFloat(quads[0]), parseFloat(quads[1]));
                }

                Vector3 parseV3(int[] quads)
                {
                    return new Vector3(parseFloat(quads[0]), parseFloat(quads[1]), parseFloat(quads[2]));
                }

                string checkField(string expectedValue)
                {
                    string what = string.Format("{0}.{1}", curNode, curField);
                    int len = readInt(what + " length");
                    bool isExpectedLen = expectedValue == null || len == expectedValue.Length;
                    if (isExpectedLen && fillQueue(len))
                    {
                        var sb = new StringBuilder();
                        for (int i = 0; i < len; i++)
                        {
                            sb.Append((char)lookaheadQueue.Dequeue());
                        }
                        var value = sb.ToString();
                        if (expectedValue != null && value != expectedValue)
                        {
                            throw new Exception(string.Format("expected {0} = {1}, got {2}",
                                                              what, expectedValue, value));
                        }
                        skipBytes(stringPadding(len), what + " padding");
                        skipBytes(4, what + ".fieldFlags");
                        return value;
                    }
                    else if (!isExpectedLen)
                    {
                        throw new Exception(string.Format("expected {0} = {1} (length {2}), got length {3}",
                                                          what, expectedValue, expectedValue.Length, len));
                    }
                    else
                    {
                        throw new Exception(string.Format("EOF reading {0} byte string for {1}", len, what));
                    }
                }

                List<double> screenArea = null;
                while (screenArea == null && readNextIdentifier()) //order important: don't eat input if got screenArea
                {
                    if (curNode == NodeType.Texture2 && curField == FieldName.filename)
                    {
                        textureFile = checkField(null);
                        if (onlyGetImageFilename)
                        {
                            return null;
                        }
                    }
                    else if (curNode == NodeType.Texture2 && curField == FieldName.wrapS)
                    {
                        checkField("CLAMP");
                    }
                    else if (curNode == NodeType.Texture2 && curField == FieldName.wrapT)
                    {
                        checkField("CLAMP");
                    }
                    else if (curNode == NodeType.ShapeHints && curField == FieldName.vertexOrdering)
                    {
                        checkField("COUNTERCLOCKWISE");
                    }
                    else if (curNode == NodeType.ShapeHints && curField == FieldName.shapeType)
                    {
                        checkField("SOLID");
                    }
                    else if (curNode == NodeType.TextureCoordinateBinding && curField == FieldName.value)
                    {
                        checkField("PER_VERTEX");
                    }
                    else if (curNode == NodeType.LevelOfDetail && curField == FieldName.screenArea)
                    {
                        screenArea = new List<double>();
                        parseList<double>(screenArea, 1, quads => parseFloat(quads[0]), "LevelOfDetail.screenArea");
                    }
                }
                if (screenArea == null)
                {
                    throw new IOException("end of file while searching for LevelOfDetail");
                }

                var lodMeshes = new List<List<Mesh>>();
                var vertices = new List<Vector3>();
                var texCoords = new List<Vector2>();
                var triStripIndices = new List<int>();

                int lod = 0, patch = 0;

                while (readNextIdentifier())
                {
                    if (curNode == NodeType.LevelOfDetail)
                    {
                        //Console.WriteLine("read {0} LODs in patch {1}", lod, patch);
                        lod = 0;
                        patch++;
                        curNode = NodeType.None;
                        curField = FieldName.None;
                    }
                    else if (curNode == NodeType.Coordinate3 && curField == FieldName.point)
                    {
                        parseList<Vector3>(vertices, 3, parseV3, "Coordinate3.point");
                        curField = FieldName.None;
                    }
                    else if (curNode == NodeType.TextureCoordinate2 && curField == FieldName.point)
                    {
                        parseList<Vector2>(texCoords, 2, parseV2, "TextureCoordinate2.point");
                        curField = FieldName.None;
                    }
                    else if (curNode == NodeType.IndexedTriangleStripSet && curField == FieldName.coordIndex)
                    {
                        parseList<int>(triStripIndices, 1, v => v[0], "IndexedTriangleStripSet.coordIndex");
                        AddLODMesh(ref lod, lodMeshes, vertices, texCoords, triStripIndices, filename);
                        curField = FieldName.None;
                    }
                }
                //Console.WriteLine("read {0} LODs in patch {1}", lod, patch);

                return MergeLODMeshes(lodMeshes, screenArea.Count);
            }
        }

        private void AddLODMesh(ref int lod, List<List<Mesh>> lodMeshes, List<Vector3> vertices,
                                List<Vector2> texCoords, List<int> triStripIndices, string filename)
        {
            if (triStripIndices.Count > 0 && (vertices.Count == 0 || texCoords.Count == 0))
            {
                throw new Exception("missing vertices or texcoords for LOD " + lod);
            }
            if (lod >= lodMeshes.Count)
            {
                lodMeshes.Add(new List<Mesh>());
            }
            lodMeshes[lod].Add(ToMesh(lod, vertices, texCoords, triStripIndices, filename));
            vertices.Clear();
            texCoords.Clear();
            triStripIndices.Clear();
            lod++;
        }

        private List<Mesh> MergeLODMeshes(List<List<Mesh>> lodMeshes, int expectedNumLOD)
        {
            List<Mesh> meshes = new List<Mesh>();
            for (int i = 0; i < lodMeshes.Count; i++)
            {
                var patchMeshes = lodMeshes[i];
                if (patchMeshes.Count > 1)
                {
                    meshes.Add(MeshMerge.Merge(patchMeshes.ToArray()));
                }
                else if (patchMeshes.Count == 1)
                {
                    meshes.Add(patchMeshes.First());
                }
            }

            //I think we should really expect meshes.Count == screenArea.Count
            //unfortunately there are examples (e.g. MSL mastcam meshes) where that is not true
            //if (meshes.Count != expectedNumLOD)
            //{
            //    throw new Exception(string.Format("expected {0} LODs, got {1}", expectedNumLOD, meshes.Count));
            //}

            return meshes;
        }
            
        private Mesh ToMesh(int lod, List<Vector3> vertexPositions, List<Vector2> texCoords, List<int> triStripIndices,
                            string filename)
        {
            var mesh = new Mesh(hasUVs: true);
            mesh.Faces.Capacity = triStripIndices.Count;
            mesh.Vertices.Capacity = texCoords.Count;

            var vertexRemap = new Dictionary<int, int>(vertexPositions.Count);
            int nextTexCoord = 0, vertInStrip = 0;
            for (int i = 0; i < triStripIndices.Count; i++)
            {
                if (triStripIndices[i] < 0)
                {
                    vertInStrip = 0;
                    continue;
                }

                var vert = new Vertex(vertexPositions[triStripIndices[i]]);
                vert.UV = texCoords[nextTexCoord++];
                vertexRemap[triStripIndices[i]] = mesh.Vertices.Count;
                mesh.Vertices.Add(vert);

                if (vertInStrip >= 2)
                {
                    if (vertInStrip % 2 == 0)
                    {
                        mesh.Faces.Add(new Face(vertexRemap[triStripIndices[i - 2]],
                                                vertexRemap[triStripIndices[i - 1]],
                                                vertexRemap[triStripIndices[i]]));
                    }
                    else
                    {
                        mesh.Faces.Add(new Face(vertexRemap[triStripIndices[i - 1]],
                                                vertexRemap[triStripIndices[i - 2]],
                                                vertexRemap[triStripIndices[i]]));
                    }
                }

                vertInStrip++;
            }

            //on one hand, as a loader, we might want to try to just load the data exactly as it was in the file
            //but on the other hand, because we just got a separate pair of texcoords for every vertex *instance*
            //(yes, this is the correct interpretation of TextureCoordinateBinding.value = PER_VERTEX)
            //we generally have a very large amount of vertex duplication here
            //
            //opting to go ahead and merge identical verts right away rather than let them get out of here
            //and possibly never get dealt with
            //also, see below about normals...
            Action<string> verbose = null, warn = null;
            if (Logger != null)
            {
                verbose = msg => Logger.LogVerbose($"{msg} in {filename} LOD {lod}");
                warn = msg => Logger.LogWarn($"{msg} in {filename} LOD {lod}");
            }
            mesh.Clean(verbose: verbose, warn: warn);

            //surprisingly, IV files for IDS mesh products do not contain precomputed normals (confirmed with Oleg)
            //there is a similar set of tradeoffs for generating them here as for whether we should clean the mesh
            //
            //one thing that is bad is to generate normals *before* cleaning the mesh
            //as that often prevents vertices from being merged that really should have been
            //and effectively creates a lot of crease points in the mesh (multiple normals for same vertex position)
            //which really should have had smooth normals (one averaged normal for that vertex position)
            //
            //opting to compute normals right away rather than let the mesh out of here without them
            mesh.GenerateVertexNormals();

            //Console.WriteLine("LOD {0} mesh {1} verts, {2} texcoords, {3} indices -> {4} verts, {5} tris (cleaned)",
            //                  lod, vertexPositions.Count, texCoords.Count, triStripIndices.Count,
            //                  mesh.Vertices.Count, mesh.Faces.Count);

            return mesh;
        }
    }
}
