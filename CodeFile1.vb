Imports System.IO
Imports System.Text
Imports System.Xml.Serialization
Imports System.Text.RegularExpressions
Imports System.Data
Imports System.Globalization
Imports System.Linq

' ===== XML fejléc és séma típusok =====

<Serializable>
Public Class MetaInfo
    Public Property DatabaseName As String
    Public Property Created As DateTime
    Public Property Version As String
    Public Property Status As String
End Class

<Serializable>
Public Class Header
    Public Property Meta As MetaInfo
    Public Property DataStartOffset As Integer
    Public Property Tables As List(Of TableDef)
End Class

<Serializable>
Public Class TableDef
    Public Property Id As Integer
    Public Property Name As String
    Public Property Columns As List(Of ColumnDef)
    Public Property PrimaryKey As String
    Public Property Indexes As List(Of IndexDef)
End Class

<Serializable>
Public Class ColumnDef
    Public Property Name As String
    Public Property [Type] As String      ' String, Int32, Int64, Double, Boolean, DateTime, Blob
    Public Property Nullable As Boolean
    Public Property DefaultValue As String
End Class

<Serializable>
Public Class IndexDef
    Public Property Name As String
    Public Property Column As String
    Public Property Unique As Boolean
End Class

' ===== FDB1 motor =====

Public Class MiniDb
    ' ---- Konstansok ----
    Private Const MAGIC As String = "FDB1"
    Private Const VERSION As Integer = 1

    ' ---- Trigger események ----
    Public Event BeforeInsert(tableName As String, values As Dictionary(Of String, Object))
    Public Event AfterInsert(tableName As String, values As Dictionary(Of String, Object))
    Public Event BeforeUpdate(tableName As String, oldValues As Dictionary(Of String, Object), newValues As Dictionary(Of String, Object))
    Public Event AfterUpdate(tableName As String, oldValues As Dictionary(Of String, Object), newValues As Dictionary(Of String, Object))
    Public Event BeforeDelete(tableName As String, values As Dictionary(Of String, Object))
    Public Event AfterDelete(tableName As String, values As Dictionary(Of String, Object))

    ' ---- Belső runtime ----
    Private Class TableRuntime
        Public Def As TableDef
        Public ColumnIndex As Dictionary(Of String, Integer)
        Public IndexData As Dictionary(Of String, Dictionary(Of Object, List(Of Long)))
        Public PrimaryKeyColumn As String
        Public PrimaryKeyIndexName As String
    End Class

    Private Class OrderKey
        Public Property Col As String
        Public Property Desc As Boolean
        Public Sub New(col As String, desc As Boolean)
            Me.Col = col : Me.Desc = desc
        End Sub
    End Class

    Private Structure RecordRef
        Public Offset As Long
        Public TableId As Integer
        Public Flags As Byte
        Public Values As Object()
        Public ReadOnly Property Deleted As Boolean
            Get
                Return (Flags And 1) = 1
            End Get
        End Property
    End Structure

    ' ---- WAL rekord típusok ----
    Private Enum WalOp As Byte
        Insert = 1
        Update = 2
        Delete = 3
    End Enum

    ' Insert/Update WAL-hoz a duplikált visszajátszás elkerüléséhez eltároljuk a fő fájl korábbi hosszát
    Private Class WalWriter
        Private ReadOnly _walPath As String
        Public Sub New(walPath As String)
            _walPath = walPath
        End Sub

        Public Sub AppendInsert(tableId As Integer, payload As Byte(), mainFileLenBefore As Long)
            Using fs = New FileStream(_walPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)
                Using bw As New BinaryWriter(fs, Encoding.UTF8, leaveOpen:=True)
                    bw.Write(CByte(WalOp.Insert))
                    bw.Write(mainFileLenBefore)
                    bw.Write(tableId)
                    bw.Write(CByte(0)) ' flags
                    bw.Write(payload.Length)
                    bw.Write(payload)
                    bw.Flush()
                    fs.Flush(True)
                End Using
            End Using
        End Sub

        Public Sub AppendUpdate(oldOffset As Long, tableId As Integer, payload As Byte(), mainFileLenBefore As Long)
            Using fs = New FileStream(_walPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)
                Using bw As New BinaryWriter(fs, Encoding.UTF8, leaveOpen:=True)
                    bw.Write(CByte(WalOp.Update))
                    bw.Write(oldOffset)
                    bw.Write(mainFileLenBefore)
                    bw.Write(tableId)
                    bw.Write(CByte(0)) ' flags
                    bw.Write(payload.Length)
                    bw.Write(payload)
                    bw.Flush()
                    fs.Flush(True)
                End Using
            End Using
        End Sub

        Public Sub AppendDelete(offset As Long)
            Using fs = New FileStream(_walPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)
                Using bw As New BinaryWriter(fs, Encoding.UTF8, leaveOpen:=True)
                    bw.Write(CByte(WalOp.Delete))
                    bw.Write(offset)
                    bw.Flush()
                    fs.Flush(True)
                End Using
            End Using
        End Sub

        Public Shared Sub Truncate(walPath As String)
            If Not File.Exists(walPath) Then Return
            Using fs = New FileStream(walPath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite)
                fs.SetLength(0)
                fs.Flush(True)
            End Using
        End Sub
    End Class

    ' ---- Állapot ----
    Private ReadOnly _path As String
    Private ReadOnly _walPath As String
    Private _header As Header
    Private _tables As Dictionary(Of String, TableRuntime)

    ' ---- Konstruktor ----
    Public Sub New(path As String, Optional databaseName As String = "Database", Optional version As String = "1.0", Optional status As String = "Active")
        _path = path
        _walPath = path & ".wal"
        _tables = New Dictionary(Of String, TableRuntime)(StringComparer.OrdinalIgnoreCase)

        If Not File.Exists(_path) Then
            ' Új adatbázis létrehozása
            _header = New Header With {
                .Meta = New MetaInfo With {.DatabaseName = databaseName, .Created = DateTime.Now, .Version = version, .Status = status},
                .DataStartOffset = 0,
                .Tables = New List(Of TableDef)()
            }
            WriteHeaderXml(initial:=True)
        Else
            ReadHeaderXml()
        End If

        BuildRuntime()
        ' WAL visszajátszás induláskor (áramszünet után)
        ReplayWalIfAny()
        ' Indexek építése
        BuildIndexes()
    End Sub

    ' ===== Fejléc írás/olvasás (short-lived I/O) =====

    Private Sub WriteHeaderXml(Optional initial As Boolean = False)
        Dim serializer As New XmlSerializer(GetType(Header))
        Dim headerBytes As Byte()
        Using ms As New MemoryStream()
            serializer.Serialize(ms, _header)
            headerBytes = ms.ToArray()
        End Using

        Using fs = New FileStream(_path, If(initial, FileMode.CreateNew, FileMode.OpenOrCreate), FileAccess.ReadWrite, FileShare.ReadWrite)
            fs.Position = 0
            Using bw As New BinaryWriter(fs, Encoding.UTF8, leaveOpen:=True)
                bw.Write(Encoding.ASCII.GetBytes(MAGIC)) ' 4
                bw.Write(VERSION)                        ' 4
                bw.Write(headerBytes.Length)             ' 4
                bw.Write(headerBytes)                    ' N
            End Using

            If initial Then
                _header.DataStartOffset = CInt(fs.Position)
                ' header újraírás friss offsettel
                Dim bytes2 As Byte()
                Using ms2 As New MemoryStream()
                    serializer.Serialize(ms2, _header)
                    bytes2 = ms2.ToArray()
                End Using
                fs.Position = 0
                Using bw As New BinaryWriter(fs, Encoding.UTF8, leaveOpen:=True)
                    bw.Write(Encoding.ASCII.GetBytes(MAGIC))
                    bw.Write(VERSION)
                    bw.Write(bytes2.Length)
                    bw.Write(bytes2)
                End Using
            End If

            fs.Flush(True)
        End Using
    End Sub

    Private Sub SaveHeaderXml()
        WriteHeaderXml(initial:=False)
    End Sub

    Private Sub ReadHeaderXml()
        Using fs = New FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
            fs.Position = 0
            Using br As New BinaryReader(fs, Encoding.UTF8, leaveOpen:=True)
                Dim magic = Encoding.ASCII.GetString(br.ReadBytes(4))
                If magic <> magic Then Throw New InvalidDataException("Érvénytelen fájlformátum (MAGIC).")
                Dim ver = br.ReadInt32()
                If ver <> VERSION Then Throw New InvalidDataException("Nem támogatott fájlverzió.")
                Dim len = br.ReadInt32()
                Dim bytes = br.ReadBytes(len)
                Using ms As New MemoryStream(bytes)
                    Dim serializer As New XmlSerializer(GetType(Header))
                    _header = CType(serializer.Deserialize(ms), Header)
                End Using
            End Using
        End Using
        If _header Is Nothing Then Throw New InvalidDataException("Fejléc betöltési hiba.")
        If _header.Tables Is Nothing Then _header.Tables = New List(Of TableDef)()
        If _header.Meta Is Nothing Then _header.Meta = New MetaInfo With {.DatabaseName = "Database", .Created = DateTime.Now, .Version = "1.0", .Status = "Active"}
    End Sub

    ' ===== Runtime és indexek =====

    Private Sub BuildRuntime()
        _tables.Clear()
        For Each t In _header.Tables
            Dim rt = New TableRuntime With {
                .Def = t,
                .ColumnIndex = New Dictionary(Of String, Integer)(StringComparer.OrdinalIgnoreCase),
                .IndexData = New Dictionary(Of String, Dictionary(Of Object, List(Of Long)))(StringComparer.OrdinalIgnoreCase),
                .PrimaryKeyColumn = t.PrimaryKey,
                .PrimaryKeyIndexName = If(String.IsNullOrEmpty(t.PrimaryKey), Nothing, $"PK_{t.Name}")
            }
            For i = 0 To t.Columns.Count - 1
                rt.ColumnIndex(t.Columns(i).Name) = i
            Next
            If Not String.IsNullOrEmpty(rt.PrimaryKeyColumn) Then
                rt.IndexData(rt.PrimaryKeyIndexName) = New Dictionary(Of Object, List(Of Long))()
            End If
            If t.Indexes IsNot Nothing Then
                For Each ix In t.Indexes
                    rt.IndexData(ix.Name) = New Dictionary(Of Object, List(Of Long))()
                Next
            End If
            _tables(t.Name) = rt
        Next
    End Sub

    Private Sub BuildIndexes()
        For Each kv In _tables
            Dim rt = kv.Value
            If rt.IndexData Is Nothing OrElse rt.IndexData.Count = 0 Then Continue For
            For Each rec In EnumerateRecords(rt.Def.Id)
                If rec.Deleted Then Continue For
                If Not String.IsNullOrEmpty(rt.PrimaryKeyColumn) Then
                    Dim pkIdx = rt.ColumnIndex(rt.PrimaryKeyColumn)
                    AddIndexEntry(rt, rt.PrimaryKeyIndexName, rec.Values(pkIdx), rec.Offset)
                End If
                If rt.Def.Indexes IsNot Nothing Then
                    For Each ix In rt.Def.Indexes
                        Dim ci = rt.ColumnIndex(ix.Column)
                        AddIndexEntry(rt, ix.Name, rec.Values(ci), rec.Offset)
                    Next
                End If
            Next
        Next
    End Sub

    Private Sub AddIndexEntry(rt As TableRuntime, indexName As String, key As Object, offset As Long)
        Dim map = rt.IndexData(indexName)
        Dim list As List(Of Long) = Nothing
        If Not map.TryGetValue(key, list) Then
            list = New List(Of Long)()
            map(key) = list
        End If
        list.Add(offset)
    End Sub

    Private Sub RemoveIndexEntry(rt As TableRuntime, indexName As String, key As Object, offset As Long)
        Dim map As Dictionary(Of Object, List(Of Long)) = Nothing
        If Not rt.IndexData.TryGetValue(indexName, map) Then Return
        Dim list As List(Of Long) = Nothing
        If Not map.TryGetValue(key, list) Then Return
        list.Remove(offset)
        If list.Count = 0 Then map.Remove(key)
    End Sub

    ' ===== Metaadatok =====

    Public Sub SetMeta(databaseName As String, version As String, status As String)
        _header.Meta.DatabaseName = databaseName
        _header.Meta.Version = version
        _header.Meta.Status = status
        SaveHeaderXml()
    End Sub

    Public Function GetMeta() As MetaInfo
        Return _header.Meta
    End Function

    ' ===== DDL: CREATE TABLE =====

    Public Sub CreateTable(name As String, columns As List(Of ColumnDef), Optional primaryKey As String = Nothing, Optional indexes As List(Of IndexDef) = Nothing)
        If _tables.ContainsKey(name) Then Throw New InvalidOperationException("A tábla már létezik.")
        Dim nextId = If(_header.Tables.Count = 0, 1, _header.Tables.Max(Function(tbl) tbl.Id) + 1)
        Dim t = New TableDef With {
            .Id = nextId,
            .Name = name,
            .Columns = columns,
            .PrimaryKey = primaryKey,
            .Indexes = If(indexes, New List(Of IndexDef)())
        }
        _header.Tables.Add(t)
        SaveHeaderXml()
        BuildRuntime()
    End Sub

    ' ===== DML: INSERT (UNIQUE + WAL + Triggerek) =====

    Public Sub Insert(tableName As String, values As Dictionary(Of String, Object))
        Dim rt = GetTable(tableName)
        ' Triggerek előtt
        RaiseEvent BeforeInsert(tableName, New Dictionary(Of String, Object)(values, StringComparer.OrdinalIgnoreCase))

        Dim vals(rt.Def.Columns.Count - 1) As Object
        For i = 0 To rt.Def.Columns.Count - 1
            Dim c = rt.Def.Columns(i)
            Dim v As Object = Nothing
            If values IsNot Nothing AndAlso values.TryGetValue(c.Name, v) Then
                vals(i) = NormalizeType(v, c.Type, c.Nullable)
            Else
                Dim defObj As Object = Nothing
                If TryParseDefault(c, defObj) Then
                    vals(i) = defObj
                Else
                    If c.Nullable Then
                        vals(i) = Nothing
                    Else
                        Throw New ArgumentException($"Hiányzó kötelező oszlop: {c.Name}")
                    End If
                End If
            End If
        Next

        EnforceUniqueness(rt, vals, existingOffsetsToIgnore:=Nothing)

        ' Sor szerializálása
        Dim payload = SerializeRow(rt.Def, vals)

        ' WAL -> Main append (short-lived)
        Dim mainLenBefore As Long
        Using fs = New FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
            mainLenBefore = fs.Length
        End Using

        Dim wal = New WalWriter(_walPath)
        wal.AppendInsert(rt.Def.Id, payload, mainLenBefore)

        Dim newOffset As Long
        Using fs = New FileStream(_path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite)
            fs.Position = fs.Length
            newOffset = fs.Position
            Using bw As New BinaryWriter(fs, Encoding.UTF8, leaveOpen:=True)
                bw.Write(rt.Def.Id)
                bw.Write(CByte(0))
                bw.Write(payload.Length)
                bw.Write(payload)
                bw.Flush()
                fs.Flush(True)
            End Using
        End Using

        WalWriter.Truncate(_walPath)

        ' Indexek
        If Not String.IsNullOrEmpty(rt.PrimaryKeyColumn) Then
            Dim pkIdx = rt.ColumnIndex(rt.PrimaryKeyColumn)
            AddIndexEntry(rt, rt.PrimaryKeyIndexName, vals(pkIdx), newOffset)
        End If
        If rt.Def.Indexes IsNot Nothing Then
            For Each ix In rt.Def.Indexes
                Dim ci = rt.ColumnIndex(ix.Column)
                AddIndexEntry(rt, ix.Name, vals(ci), newOffset)
            Next
        End If

        ' Triggerek után
        RaiseEvent AfterInsert(tableName, ProjectInsertDict(rt, vals))
    End Sub

    Private Function ProjectInsertDict(rt As TableRuntime, vals As Object()) As Dictionary(Of String, Object)
        Dim d As New Dictionary(Of String, Object)(StringComparer.OrdinalIgnoreCase)
        For i = 0 To rt.Def.Columns.Count - 1
            d(rt.Def.Columns(i).Name) = vals(i)
        Next
        Return d
    End Function

    ' ===== DML: UPDATE (append + tombstone, WAL + Triggerek) =====

    Private Function GetMatchingOffsets(rt As TableRuntime, where As Dictionary(Of String, Object)) As IEnumerable(Of Long)
        Dim candidateOffsets As IEnumerable(Of Long) = Nothing

        If where IsNot Nothing AndAlso where.Count = 1 Then
            Dim wCol = where.Keys.First()
            Dim wVal = where(wCol)

            ' PK?
            If Not String.IsNullOrEmpty(rt.PrimaryKeyColumn) AndAlso String.Equals(wCol, rt.PrimaryKeyColumn, StringComparison.OrdinalIgnoreCase) Then
                Dim key = NormalizeType(wVal, rt.Def.Columns(rt.ColumnIndex(wCol)).Type, True)
                Dim map = rt.IndexData(rt.PrimaryKeyIndexName)
                Dim list As List(Of Long) = Nothing
                If map.TryGetValue(key, list) Then
                    candidateOffsets = list
                Else
                    candidateOffsets = Enumerable.Empty(Of Long)()
                End If

                ' Egyéb index?
            ElseIf rt.Def.Indexes IsNot Nothing Then
                Dim ix = rt.Def.Indexes.FirstOrDefault(Function(ixd) String.Equals(ixd.Column, wCol, StringComparison.OrdinalIgnoreCase))
                If ix IsNot Nothing Then
                    Dim key = NormalizeType(wVal, rt.Def.Columns(rt.ColumnIndex(wCol)).Type, True)
                    Dim map = rt.IndexData(ix.Name)
                    Dim list As List(Of Long) = Nothing
                    If map.TryGetValue(key, list) Then
                        candidateOffsets = list
                    Else
                        candidateOffsets = Enumerable.Empty(Of Long)()
                    End If
                End If
            End If
        End If

        If candidateOffsets Is Nothing Then
            ' Teljes bejárás, WHERE ellenőrzéssel
            Return EnumerateRecords(rt.Def.Id).
               Where(Function(r) Not r.Deleted AndAlso MatchesWhere(rt, r, where)).
               Select(Function(r) r.Offset)
        Else
            ' Jelöltek validálása WHERE-rel
            Dim offs As New List(Of Long)
            For Each off In candidateOffsets
                Dim rec = ReadRecordAt(off)
                If rec.TableId <> rt.Def.Id OrElse rec.Deleted Then Continue For
                If MatchesWhere(rt, rec, where) Then offs.Add(off)
            Next
            Return offs
        End If
    End Function

    Public Sub UpdateRows(tableName As String, setValues As Dictionary(Of String, Object), where As Dictionary(Of String, Object))
        Dim rt = GetTable(tableName)
        Dim matchOffsets = GetMatchingOffsets(rt, where).ToList()

        For Each oldOff In matchOffsets
            Dim rec = ReadRecordAt(oldOff)
            If rec.Deleted Then Continue For

            Dim oldDict = Project(rt, rec, New List(Of String) From {"*"})
            Dim newVals = CType(rec.Values.Clone(), Object())

            For Each kv In setValues
                Dim colIdx = rt.ColumnIndex(kv.Key)
                Dim colDef = rt.Def.Columns(colIdx)
                newVals(colIdx) = NormalizeType(kv.Value, colDef.Type, colDef.Nullable)
            Next

            ' Triggerek előtt
            RaiseEvent BeforeUpdate(tableName, oldDict, DictFromVals(rt, newVals))

            EnforceUniqueness(rt, newVals, existingOffsetsToIgnore:=New HashSet(Of Long) From {oldOff})
            Dim payload = SerializeRow(rt.Def, newVals)

            Dim mainLenBefore As Long
            Using fs = New FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
                mainLenBefore = fs.Length
            End Using

            Dim wal = New WalWriter(_walPath)
            wal.AppendUpdate(oldOff, rt.Def.Id, payload, mainLenBefore)

            ' 1) tombstone a régi rekordra
            MarkDeleted(oldOff)

            ' 2) új rekord beírása
            Dim newOff As Long
            Using fs = New FileStream(_path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite)
                fs.Position = fs.Length
                newOff = fs.Position
                Using bw As New BinaryWriter(fs, Encoding.UTF8, leaveOpen:=True)
                    bw.Write(rt.Def.Id)
                    bw.Write(CByte(0))
                    bw.Write(payload.Length)
                    bw.Write(payload)
                    bw.Flush()
                    fs.Flush(True)
                End Using
            End Using

            WalWriter.Truncate(_walPath)

            ' Indexek frissítése
            If Not String.IsNullOrEmpty(rt.PrimaryKeyColumn) Then
                Dim pkIdx = rt.ColumnIndex(rt.PrimaryKeyColumn)
                RemoveIndexEntry(rt, rt.PrimaryKeyIndexName, rec.Values(pkIdx), oldOff)
                AddIndexEntry(rt, rt.PrimaryKeyIndexName, newVals(pkIdx), newOff)
            End If
            If rt.Def.Indexes IsNot Nothing Then
                For Each ix In rt.Def.Indexes
                    Dim ci = rt.ColumnIndex(ix.Column)
                    RemoveIndexEntry(rt, ix.Name, rec.Values(ci), oldOff)
                    AddIndexEntry(rt, ix.Name, newVals(ci), newOff)
                Next
            End If

            ' Triggerek után
            RaiseEvent AfterUpdate(tableName, oldDict, DictFromVals(rt, newVals))
        Next
    End Sub

    ' ===== DML: DELETE (tombstone, WAL + Triggerek) =====

    Public Sub DeleteRows(tableName As String, where As Dictionary(Of String, Object))
        Dim rt = GetTable(tableName)
        Dim matchOffsets = GetMatchingOffsets(rt, where).ToList()

        For Each off In matchOffsets
            Dim rec = ReadRecordAt(off)
            If rec.Deleted Then Continue For

            Dim oldDict = Project(rt, rec, New List(Of String) From {"*"})

            ' Triggerek előtt
            RaiseEvent BeforeDelete(tableName, oldDict)

            ' WAL
            Dim wal = New WalWriter(_walPath)
            wal.AppendDelete(off)

            ' Flag beállítás
            MarkDeleted(off)

            WalWriter.Truncate(_walPath)

            ' Index eltávolítás
            If Not String.IsNullOrEmpty(rt.PrimaryKeyColumn) Then
                Dim pkIdx = rt.ColumnIndex(rt.PrimaryKeyColumn)
                RemoveIndexEntry(rt, rt.PrimaryKeyIndexName, rec.Values(pkIdx), off)
            End If
            If rt.Def.Indexes IsNot Nothing Then
                For Each ix In rt.Def.Indexes
                    Dim ci = rt.ColumnIndex(ix.Column)
                    RemoveIndexEntry(rt, ix.Name, rec.Values(ci), off)
                Next
            End If

            ' Triggerek után
            RaiseEvent AfterDelete(tableName, oldDict)
        Next
    End Sub

    Private Sub MarkDeleted(offset As Long)
        Using fs = New FileStream(_path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite)
            fs.Position = offset + 4 ' tableId után a flags
            Dim cur = CByte(fs.ReadByte())
            fs.Position = offset + 4
            fs.WriteByte(CByte(cur Or 1))
            fs.Flush(True)
        End Using
    End Sub

    ' ===== DQL: SELECT (WHERE AND, ORDER BY, LIMIT) =====

    Public Function SelectRows(tableName As String, columns As List(Of String), Optional where As Dictionary(Of String, Object) = Nothing) As List(Of Dictionary(Of String, Object))
        Dim rt = GetTable(tableName)
        Dim result As New List(Of Dictionary(Of String, Object))()

        Dim candidateOffsets As IEnumerable(Of Long) = Nothing

        If where IsNot Nothing AndAlso where.Count = 1 AndAlso rt.IndexData IsNot Nothing AndAlso rt.IndexData.Count > 0 Then
            Dim wCol = where.Keys.First()
            If Not String.IsNullOrEmpty(rt.PrimaryKeyColumn) AndAlso String.Equals(wCol, rt.PrimaryKeyColumn, StringComparison.OrdinalIgnoreCase) Then
                Dim col = rt.Def.Columns(rt.ColumnIndex(wCol))
                Dim key = NormalizeType(where(wCol), col.Type, True)
                Dim map = rt.IndexData(rt.PrimaryKeyIndexName)
                Dim list As List(Of Long) = Nothing
                If map.TryGetValue(key, list) Then
                    candidateOffsets = list
                Else
                    candidateOffsets = Enumerable.Empty(Of Long)()
                End If
            ElseIf rt.Def.Indexes IsNot Nothing AndAlso rt.Def.Indexes.Count > 0 Then
                Dim ix = rt.Def.Indexes.FirstOrDefault(Function(i) String.Equals(i.Column, wCol, StringComparison.OrdinalIgnoreCase))
                If ix IsNot Nothing Then
                    Dim col = rt.Def.Columns(rt.ColumnIndex(wCol))
                    Dim key = NormalizeType(where(wCol), col.Type, True)
                    Dim map = rt.IndexData(ix.Name)
                    Dim list As List(Of Long) = Nothing
                    If map.TryGetValue(key, list) Then
                        candidateOffsets = list
                    Else
                        candidateOffsets = Enumerable.Empty(Of Long)()
                    End If
                End If
            End If
        End If

        If candidateOffsets Is Nothing Then
            For Each rec In EnumerateRecords(rt.Def.Id)
                If rec.Deleted Then Continue For
                If MatchesWhere(rt, rec, where) Then
                    result.Add(Project(rt, rec, columns))
                End If
            Next
        Else
            For Each off In candidateOffsets
                Dim rec = ReadRecordAt(off)
                If rec.TableId <> rt.Def.Id OrElse rec.Deleted Then Continue For
                If MatchesWhere(rt, rec, where) Then
                    result.Add(Project(rt, rec, columns))
                End If
            Next
        End If

        Return result
    End Function

    ' ===== DataTable export =====

    Public Function ToDataTable(tableName As String, Optional where As Dictionary(Of String, Object) = Nothing) As DataTable
        Dim rt = GetTable(tableName)
        Dim dt As New DataTable(rt.Def.Name)
        For Each c In rt.Def.Columns
            dt.Columns.Add(c.Name, MapTypeToClr(c.Type))
        Next
        Dim rows = SelectRows(tableName, New List(Of String) From {"*"}, where)
        For Each r In rows
            Dim arr(rt.Def.Columns.Count - 1) As Object
            For i = 0 To rt.Def.Columns.Count - 1
                arr(i) = r(rt.Def.Columns(i).Name)
            Next
            dt.Rows.Add(arr)
        Next
        Return dt
    End Function

    ' ===== SQL végrehajtó =====

    Public Function ExecuteQuery(sql As String) As List(Of Dictionary(Of String, Object))
        sql = sql.Trim()
        If sql.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) Then
            Return ParseSelect(sql)
        ElseIf sql.StartsWith("INSERT", StringComparison.OrdinalIgnoreCase) Then
            ParseInsert(sql)
            Return New List(Of Dictionary(Of String, Object))()
        ElseIf Regex.IsMatch(sql, "^\s*CREATE\s+TABLE", RegexOptions.IgnoreCase) Then
            ParseCreateTable(sql)
            Return New List(Of Dictionary(Of String, Object))()
        ElseIf sql.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase) Then
            ParseUpdate(sql)
            Return New List(Of Dictionary(Of String, Object))()
        ElseIf Regex.IsMatch(sql, "^\s*DELETE\s+FROM", RegexOptions.IgnoreCase) Then
            ParseDelete(sql)
            Return New List(Of Dictionary(Of String, Object))()
        Else
            Throw New NotSupportedException("Csak SELECT, INSERT, CREATE TABLE, UPDATE, DELETE támogatott jelenleg.")
        End If
    End Function

    ' ===== Parser: SELECT (ORDER BY + LIMIT) =====

    Private Function IndexOfKeywordOutsideQuotes(text As String, keyword As String) As Integer
        Dim inStr = False
        Dim quote As Char = Chr(0)
        Dim i = 0
        While i < text.Length
            Dim ch = text(i)
            If inStr Then
                If ch = quote Then inStr = False
                i += 1
            Else
                If ch = "'"c OrElse ch = """"c OrElse ch = "`"c OrElse ch = "["c Then
                    inStr = True
                    quote = If(ch = "["c, "]"c, ch)
                    i += 1
                ElseIf Char.ToUpperInvariant(ch) = Char.ToUpperInvariant(keyword(0)) Then
                    If i + keyword.Length <= text.Length AndAlso
                   String.Compare(text, i, keyword, 0, keyword.Length, True, Globalization.CultureInfo.InvariantCulture) = 0 Then
                        Return i
                    End If
                    i += 1
                Else
                    i += 1
                End If
            End If
        End While
        Return -1
    End Function

    Private Function ParseSelect(sql As String) As List(Of Dictionary(Of String, Object))
        Dim pattern As String =
            "SELECT\s+(?<cols>.+?)\s+FROM\s+(?<tbl>.+?)" &
            "(?:\s+WHERE\s+(?<where>.+?))?" &
            "(?:\s+ORDER\s+BY\s+(?<orderby>.+?))?" &
            "(?:\s+LIMIT\s+(?<limit>.+))?$"

        Dim m = Regex.Match(sql, pattern, RegexOptions.IgnoreCase)
        If Not m.Success Then Throw New ArgumentException("Hibás SELECT.")

        Dim colsPart = m.Groups("cols").Value.Trim()
        Dim cols As List(Of String)
        If colsPart = "*" Then
            cols = New List(Of String) From {"*"}
        Else
            cols = SplitCsvRespectQuotes(colsPart).Select(AddressOf UnquoteIdentifier).ToList()
        End If

        Dim tbl = UnquoteIdentifier(m.Groups("tbl").Value.Trim())

        Dim whereDict As Dictionary(Of String, Object) = Nothing
        If m.Groups("where").Success Then
            whereDict = New Dictionary(Of String, Object)(StringComparer.OrdinalIgnoreCase)
            Dim whereText = m.Groups("where").Value
            Dim andParts = SplitAndConditions(whereText)
            For Each p In andParts
                Dim mm = Regex.Match(p.Trim(), "^(?<col>.+?)\s*=\s*(?<val>.+)$", RegexOptions.IgnoreCase)
                If Not mm.Success Then Throw New ArgumentException("Csak '=' és 'AND' támogatott WHERE-ben.")
                Dim colNameParsed = UnquoteIdentifier(mm.Groups("col").Value.Trim())
                Dim lit = mm.Groups("val").Value.Trim()
                whereDict(colNameParsed) = ParseLiteral(lit)
            Next
        End If

        Dim result = SelectRows(tbl, cols, whereDict)

        Dim orderByCols As New List(Of OrderKey)
        If m.Groups("orderby").Success Then
            Dim obPart = m.Groups("orderby").Value.Trim()
            Dim limitIdx = IndexOfKeywordOutsideQuotes(obPart, "LIMIT")
            If limitIdx >= 0 Then obPart = obPart.Substring(0, limitIdx).Trim()
            Dim obCols = SplitCsvRespectQuotes(obPart)
            For Each ob In obCols
                Dim parts = ob.Trim().Split({" "}, StringSplitOptions.RemoveEmptyEntries)
                Dim colNameParsed = UnquoteIdentifier(parts(0))
                Dim desc = (parts.Length > 1 AndAlso parts(1).Equals("DESC", StringComparison.OrdinalIgnoreCase))
                orderByCols.Add(New OrderKey(colNameParsed, desc))
            Next
        End If

        If orderByCols.Count > 0 AndAlso result.Count > 1 Then
            Dim ordered As IOrderedEnumerable(Of Dictionary(Of String, Object)) = Nothing
            For i = 0 To orderByCols.Count - 1
                Dim ob = orderByCols(i)
                If ordered Is Nothing Then
                    ordered = If(ob.Desc,
                        result.OrderByDescending(Function(r) r(ob.Col), Comparer(Of Object).Default),
                        result.OrderBy(Function(r) r(ob.Col), Comparer(Of Object).Default))
                Else
                    ordered = If(ob.Desc,
                        ordered.ThenByDescending(Function(r) r(ob.Col), Comparer(Of Object).Default),
                        ordered.ThenBy(Function(r) r(ob.Col), Comparer(Of Object).Default))
                End If
            Next
            result = ordered.ToList()
        End If

        If m.Groups("limit").Success Then
            Dim limText = m.Groups("limit").Value.Trim()
            Dim limitCount As Integer? = Nothing
            Dim limitOffset As Integer = 0

            Dim mo = Regex.Match(limText, "^(?<count>\d+)\s+OFFSET\s+(?<offset>\d+)$", RegexOptions.IgnoreCase)
            If mo.Success Then
                limitCount = Integer.Parse(mo.Groups("count").Value, CultureInfo.InvariantCulture)
                limitOffset = Integer.Parse(mo.Groups("offset").Value, CultureInfo.InvariantCulture)
            Else
                Dim parts = SplitCsvRespectQuotes(limText)
                If parts.Count = 1 Then
                    Dim c As Integer
                    If Integer.TryParse(parts(0).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, c) Then
                        limitCount = c
                    Else
                        Throw New ArgumentException("Hibás LIMIT érték.")
                    End If
                ElseIf parts.Count = 2 Then
                    Dim o As Integer, c As Integer
                    If Integer.TryParse(parts(0).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, o) AndAlso
                       Integer.TryParse(parts(1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, c) Then
                        limitOffset = o
                        limitCount = c
                    Else
                        Throw New ArgumentException("Hibás LIMIT offset,count érték.")
                    End If
                Else
                    Throw New ArgumentException("Hibás LIMIT szintaxis.")
                End If
            End If

            If limitCount.HasValue Then
                If limitOffset > 0 Then result = result.Skip(limitOffset).ToList()
                result = result.Take(Math.Max(0, limitCount.Value)).ToList()
            End If
        End If

        Return result
    End Function

    ' ===== Parser: INSERT =====

    Private Function SplitCsvLiterals(s As String) As List(Of String)
        Dim res As New List(Of String)
        Dim cur As New StringBuilder()
        Dim inQuotes As Boolean = False
        Dim quote As Char = Chr(0)

        For i = 0 To s.Length - 1
            Dim ch = s(i)
            If inQuotes Then
                cur.Append(ch)
                If ch = quote Then
                    inQuotes = False
                End If
            Else
                If ch = "'"c OrElse ch = """"c Then
                    inQuotes = True
                    quote = ch
                    cur.Append(ch)
                ElseIf ch = ","c Then
                    res.Add(cur.ToString().Trim())
                    cur.Clear()
                Else
                    cur.Append(ch)
                End If
            End If
        Next

        If cur.Length > 0 Then res.Add(cur.ToString().Trim())
        Return res
    End Function

    Private Sub ParseInsert(sql As String)
        Dim m = Regex.Match(sql, "INSERT\s+INTO\s+(?<tbl>.+?)\s*\((?<cols>.+?)\)\s*VALUES\s*\((?<vals>.+)\)$", RegexOptions.IgnoreCase)
        If Not m.Success Then Throw New ArgumentException("Hibás INSERT.")

        Dim tbl = UnquoteIdentifier(m.Groups("tbl").Value.Trim())
        Dim cols = SplitCsvRespectQuotes(m.Groups("cols").Value).Select(AddressOf UnquoteIdentifier).ToList()
        Dim rawVals = SplitCsvLiterals(m.Groups("vals").Value)

        If rawVals.Count <> cols.Count Then Throw New ArgumentException("Oszlop/érték darabszám eltérés.")

        Dim vals As New Dictionary(Of String, Object)(StringComparer.OrdinalIgnoreCase)
        For i = 0 To cols.Count - 1
            vals(cols(i)) = ParseLiteral(rawVals(i))
        Next

        Insert(tbl, vals)
    End Sub

    ' ===== Parser: CREATE TABLE =====

    Private Sub ParseCreateTable(sql As String)
        Dim m = Regex.Match(sql, "CREATE\s+TABLE(\s+IF\s+NOT\s+EXISTS)?\s+(?<tbl>.+?)\s*\((?<body>.+)\)$", RegexOptions.IgnoreCase)
        If Not m.Success Then Throw New ArgumentException("Hibás CREATE TABLE.")

        Dim tbl = UnquoteIdentifier(m.Groups("tbl").Value.Trim())
        If _tables.ContainsKey(tbl) Then
            Dim hasIfNotExists = Regex.IsMatch(sql, "CREATE\s+TABLE\s+IF\s+NOT\s+EXISTS", RegexOptions.IgnoreCase)
            If hasIfNotExists Then Return Else Throw New InvalidOperationException("A tábla már létezik.")
        End If

        Dim body = m.Groups("body").Value
        Dim parts = SplitTopLevel(body)

        Dim cols As New List(Of ColumnDef)
        Dim pk As String = Nothing
        Dim indexes As New List(Of IndexDef)

        For Each part In parts
            Dim p = part.Trim()
            If p.Length = 0 Then Continue For

            If p.StartsWith("PRIMARY KEY", StringComparison.OrdinalIgnoreCase) Then
                Dim mm = Regex.Match(p, "PRIMARY\s+KEY\s*\(\s*(?<col>.+?)\s*\)", RegexOptions.IgnoreCase)
                If mm.Success Then pk = UnquoteIdentifier(mm.Groups("col").Value)
                Continue For
            End If

            If p.StartsWith("UNIQUE INDEX", StringComparison.OrdinalIgnoreCase) OrElse p.StartsWith("INDEX", StringComparison.OrdinalIgnoreCase) Then
                Dim uniq = p.StartsWith("UNIQUE INDEX", StringComparison.OrdinalIgnoreCase)
                Dim mm = Regex.Match(p, "INDEX\s*\(\s*(?<col>.+?)\s*\)", RegexOptions.IgnoreCase)
                If mm.Success Then
                    Dim colNameParsed = UnquoteIdentifier(mm.Groups("col").Value)
                    indexes.Add(New IndexDef With {.Name = $"IX_{tbl}_{colNameParsed}", .Column = colNameParsed, .Unique = uniq})
                End If
                Continue For
            End If

            Dim nameTok As String = ""
            Dim rest As String = ""
            ExtractFirstToken(p, nameTok, rest)
            Dim colName = UnquoteIdentifier(nameTok)
            Dim typeTok As String = Nothing
            rest = rest.Trim()
            If rest.Length = 0 Then Throw New ArgumentException("Hiányzó típus: " & p)

            ExtractFirstToken(rest, typeTok, rest)
            Dim mappedType = MapSqlTypeToInternal(typeTok)

            Dim nullable As Boolean = True
            Dim defaultVal As String = Nothing
            Dim tail = rest.Trim()
            While tail.Length > 0
                If tail.StartsWith("NOT NULL", StringComparison.OrdinalIgnoreCase) Then
                    nullable = False
                    tail = tail.Substring(8).Trim()
                ElseIf tail.StartsWith("NULL", StringComparison.OrdinalIgnoreCase) Then
                    nullable = True
                    tail = tail.Substring(4).Trim()
                ElseIf tail.StartsWith("DEFAULT", StringComparison.OrdinalIgnoreCase) Then
                    Dim dVal As String = ""
                    Dim after As String = ""
                    ExtractDefaultExpression(tail, dVal, after)
                    defaultVal = dVal
                    tail = after.Trim()
                Else
                    Exit While
                End If
            End While

            cols.Add(New ColumnDef With {
                .Name = colName,
                .Type = mappedType,
                .Nullable = nullable,
                .DefaultValue = defaultVal
            })
        Next

        CreateTable(tbl, cols, pk, indexes)
    End Sub

    ' ===== Parser: UPDATE =====

    Private Sub ParseUpdate(sql As String)
        Dim m = Regex.Match(sql, "UPDATE\s+(?<tbl>.+?)\s+SET\s+(?<set>.+?)(?:\s+WHERE\s+(?<where>.+))?$", RegexOptions.IgnoreCase)
        If Not m.Success Then Throw New ArgumentException("Hibás UPDATE.")

        Dim tbl = UnquoteIdentifier(m.Groups("tbl").Value.Trim())
        Dim setText = m.Groups("set").Value.Trim()

        Dim setParts = SplitTopLevel(setText)
        Dim setJoined = String.Join(",", setParts)
        Dim setList = SplitCsvRespectQuotes(setJoined)

        Dim setVals As New Dictionary(Of String, Object)(StringComparer.OrdinalIgnoreCase)
        For Each s In setList
            Dim mm = Regex.Match(s.Trim(), "^(?<col>.+?)\s*=\s*(?<val>.+)$", RegexOptions.IgnoreCase)
            If Not mm.Success Then Throw New ArgumentException("Hibás SET elem: " & s)
            Dim colNameParsed = UnquoteIdentifier(mm.Groups("col").Value.Trim())
            setVals(colNameParsed) = ParseLiteral(mm.Groups("val").Value.Trim())
        Next

        Dim whereDict As Dictionary(Of String, Object) = Nothing
        If m.Groups("where").Success Then
            whereDict = New Dictionary(Of String, Object)(StringComparer.OrdinalIgnoreCase)
            Dim andParts = SplitAndConditions(m.Groups("where").Value)
            For Each p In andParts
                Dim mm = Regex.Match(p.Trim(), "^(?<col>.+?)\s*=\s*(?<val>.+)$", RegexOptions.IgnoreCase)
                If Not mm.Success Then Throw New ArgumentException("Csak '=' és 'AND' támogatott WHERE-ben.")
                Dim colNameParsed = UnquoteIdentifier(mm.Groups("col").Value.Trim())
                whereDict(colNameParsed) = ParseLiteral(mm.Groups("val").Value.Trim())
            Next
        End If

        UpdateRows(tbl, setVals, whereDict)
    End Sub

    ' ===== Parser: DELETE =====

    Private Sub ParseDelete(sql As String)
        Dim m = Regex.Match(sql, "DELETE\s+FROM\s+(?<tbl>.+?)(?:\s+WHERE\s+(?<where>.+))?$", RegexOptions.IgnoreCase)
        If Not m.Success Then Throw New ArgumentException("Hibás DELETE.")

        Dim tbl = UnquoteIdentifier(m.Groups("tbl").Value.Trim())
        Dim whereDict As Dictionary(Of String, Object) = Nothing
        If m.Groups("where").Success Then
            whereDict = New Dictionary(Of String, Object)(StringComparer.OrdinalIgnoreCase)
            Dim andParts = SplitAndConditions(m.Groups("where").Value)
            For Each p In andParts
                Dim mm = Regex.Match(p.Trim(), "^(?<col>.+?)\s*=\s*(?<val>.+)$", RegexOptions.IgnoreCase)
                If Not mm.Success Then Throw New ArgumentException("Csak '=' és 'AND' támogatott WHERE-ben.")
                Dim colNameParsed = UnquoteIdentifier(mm.Groups("col").Value.Trim())
                whereDict(colNameParsed) = ParseLiteral(mm.Groups("val").Value.Trim())
            Next
        End If

        DeleteRows(tbl, whereDict)
    End Sub

    ' ===== Uniqueness ellenőrzés =====

    Private Sub EnforceUniqueness(rt As TableRuntime, vals As Object(), existingOffsetsToIgnore As HashSet(Of Long))
        ' PK
        If Not String.IsNullOrEmpty(rt.PrimaryKeyColumn) Then
            Dim pkIdx = rt.ColumnIndex(rt.PrimaryKeyColumn)
            Dim key = vals(pkIdx)
            If key IsNot Nothing Then
                Dim map = rt.IndexData(rt.PrimaryKeyIndexName)
                Dim list As List(Of Long) = Nothing
                If map.TryGetValue(key, list) Then
                    If existingOffsetsToIgnore Is Nothing OrElse list.Any(Function(o) Not existingOffsetsToIgnore.Contains(o)) Then
                        Throw New InvalidOperationException($"PRIMARY KEY megsértése: {rt.PrimaryKeyColumn} = {key}")
                    End If
                End If
            End If
        End If
        ' UNIQUE indexek
        If rt.Def.Indexes Is Nothing Then Return
        For Each ix In rt.Def.Indexes
            If Not ix.Unique Then Continue For
            Dim ci = rt.ColumnIndex(ix.Column)
            Dim key = vals(ci)
            If key Is Nothing Then Continue For
            Dim map = rt.IndexData(ix.Name)
            Dim list As List(Of Long) = Nothing
            If map.TryGetValue(key, list) Then
                If existingOffsetsToIgnore Is Nothing OrElse list.Any(Function(o) Not existingOffsetsToIgnore.Contains(o)) Then
                    Throw New InvalidOperationException($"UNIQUE megsértése: {ix.Column} = {key}")
                End If
            End If
        Next
    End Sub

    ' ===== WAL visszajátszás induláskor =====

    Private Sub ReplayWalIfAny()
        If Not File.Exists(_walPath) Then Return
        Dim fileInfo As New FileInfo(_walPath)
        If fileInfo.Length = 0 Then Return

        Using fsWal = New FileStream(_walPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
            Using br As New BinaryReader(fsWal, Encoding.UTF8, leaveOpen:=True)
                While fsWal.Position < fsWal.Length
                    Dim op As WalOp = CType(br.ReadByte(), WalOp)
                    If op = WalOp.Insert Then
                        Dim mainLenBefore = br.ReadInt64()
                        Dim tableId = br.ReadInt32()
                        Dim flags = br.ReadByte()
                        Dim len = br.ReadInt32()
                        Dim payload = br.ReadBytes(len)
                        ' Csak akkor alkalmazzuk, ha a fő fájl hossza megegyezik a feljegyzett "előtte" értékkel
                        Dim apply As Boolean
                        Using main = New FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
                            apply = (main.Length = mainLenBefore)
                        End Using
                        If apply Then
                            Using main = New FileStream(_path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite)
                                main.Position = main.Length
                                Using bw As New BinaryWriter(main, Encoding.UTF8, leaveOpen:=True)
                                    bw.Write(tableId)
                                    bw.Write(flags)
                                    bw.Write(len)
                                    bw.Write(payload)
                                    bw.Flush()
                                    main.Flush(True)
                                End Using
                            End Using
                        End If
                    ElseIf op = WalOp.Update Then
                        Dim oldOffset = br.ReadInt64()
                        Dim mainLenBefore = br.ReadInt64()
                        Dim tableId = br.ReadInt32()
                        Dim flags = br.ReadByte()
                        Dim len = br.ReadInt32()
                        Dim payload = br.ReadBytes(len)
                        ' 1) tombstone (idempotens)
                        MarkDeleted(oldOffset)
                        ' 2) append új rekord, ha még nem történt meg
                        Dim apply As Boolean
                        Using main = New FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
                            apply = (main.Length = mainLenBefore)
                        End Using
                        If apply Then
                            Using main = New FileStream(_path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite)
                                main.Position = main.Length
                                Using bw As New BinaryWriter(main, Encoding.UTF8, leaveOpen:=True)
                                    bw.Write(tableId)
                                    bw.Write(flags)
                                    bw.Write(len)
                                    bw.Write(payload)
                                    bw.Flush()
                                    main.Flush(True)
                                End Using
                            End Using
                        End If
                    ElseIf op = WalOp.Delete Then
                        Dim offset = br.ReadInt64()
                        MarkDeleted(offset)
                    Else
                        Exit While
                    End If
                End While
            End Using
        End Using

        ' WAL kiürítése
        WalWriter.Truncate(_walPath)
    End Sub

    ' ===== Rekord bejárás / olvasás (short-lived I/O) =====

    Private Iterator Function EnumerateRecords(tableId As Integer) As IEnumerable(Of RecordRef)
        Using fs = New FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
            Dim pos As Long = _header.DataStartOffset
            While pos < fs.Length
                fs.Position = pos
                Using br As New BinaryReader(fs, Encoding.UTF8, leaveOpen:=True)
                    Dim tId = br.ReadInt32()
                    Dim flags = br.ReadByte()
                    Dim len = br.ReadInt32()
                    Dim payload = br.ReadBytes(len)
                    If tId = tableId Then
                        Dim def = _header.Tables.First(Function(tbl) tbl.Id = tableId)
                        Dim vals = DeserializeRow(def, payload)
                        Yield New RecordRef With {.Offset = pos, .TableId = tId, .Flags = flags, .Values = vals}
                    End If
                    pos += 4 + 1 + 4 + len
                End Using
            End While
        End Using
    End Function

    Private Function ReadRecordAt(offset As Long) As RecordRef
        Using fs = New FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
            fs.Position = offset
            Using br As New BinaryReader(fs, Encoding.UTF8, leaveOpen:=True)
                Dim tId = br.ReadInt32()
                Dim flags = br.ReadByte()
                Dim len = br.ReadInt32()
                Dim payload = br.ReadBytes(len)
                Dim def = _header.Tables.First(Function(tbl) tbl.Id = tId)
                Dim vals = DeserializeRow(def, payload)
                Return New RecordRef With {.Offset = offset, .TableId = tId, .Flags = flags, .Values = vals}
            End Using
        End Using
    End Function

    Private Function MatchesWhere(rt As TableRuntime, rec As RecordRef, where As Dictionary(Of String, Object)) As Boolean
        If where Is Nothing OrElse where.Count = 0 Then Return True
        For Each kv In where
            Dim colIdx = rt.ColumnIndex(kv.Key)
            Dim colType = rt.Def.Columns(colIdx).Type
            Dim w = NormalizeType(kv.Value, colType, True)
            Dim rv = rec.Values(colIdx)
            If (rv Is Nothing AndAlso w IsNot Nothing) OrElse (rv IsNot Nothing AndAlso Not Object.Equals(rv, w)) Then
                Return False
            End If
        Next
        Return True
    End Function

    Private Function Project(rt As TableRuntime, rec As RecordRef, cols As List(Of String)) As Dictionary(Of String, Object)
        Dim res As New Dictionary(Of String, Object)(StringComparer.OrdinalIgnoreCase)
        If cols Is Nothing OrElse cols.Count = 0 OrElse (cols.Count = 1 AndAlso cols(0) = "*") Then
            For i = 0 To rt.Def.Columns.Count - 1
                res(rt.Def.Columns(i).Name) = rec.Values(i)
            Next
        Else
            For Each c In cols
                res(c) = rec.Values(rt.ColumnIndex(c))
            Next
        End If
        Return res
    End Function

    Private Function DictFromVals(rt As TableRuntime, vals As Object()) As Dictionary(Of String, Object)
        Dim d As New Dictionary(Of String, Object)(StringComparer.OrdinalIgnoreCase)
        For i = 0 To rt.Def.Columns.Count - 1
            d(rt.Def.Columns(i).Name) = vals(i)
        Next
        Return d
    End Function

    Private Function GetTable(name As String) As TableRuntime
        Dim rt As TableRuntime = Nothing
        If Not _tables.TryGetValue(name, rt) Then Throw New InvalidOperationException("Tábla nem található: " & name)
        Return rt
    End Function

    ' ===== Karbantartás: COMPACT =====

    Public Sub Compact()
        Dim temp = _path & ".tmp"
        ' 1) új fájl felépítése
        Using newFs = New FileStream(temp, FileMode.Create, FileAccess.ReadWrite, FileShare.None)
            ' ideiglenes header írása
            Dim serializer As New XmlSerializer(GetType(Header))
            Dim headerBytes As Byte()
            Using ms As New MemoryStream()
                serializer.Serialize(ms, _header)
                headerBytes = ms.ToArray()
            End Using
            Using bw As New BinaryWriter(newFs, Encoding.UTF8, leaveOpen:=True)
                bw.Write(Encoding.ASCII.GetBytes(MAGIC))
                bw.Write(VERSION)
                bw.Write(headerBytes.Length)
                bw.Write(headerBytes)
            End Using
            Dim newDataStart = CInt(newFs.Position)

            ' rekordok másolása
            For Each t In _header.Tables
                For Each rec In EnumerateRecords(t.Id)
                    If rec.Deleted Then Continue For
                    Using bw As New BinaryWriter(newFs, Encoding.UTF8, leaveOpen:=True)
                        bw.Write(t.Id)
                        bw.Write(CByte(0))
                        Dim payload = SerializeRow(t, rec.Values)
                        bw.Write(payload.Length)
                        bw.Write(payload)
                    End Using
                Next
            Next
            newFs.Flush(True)

            ' fejléc frissítése új offsettel
            _header.DataStartOffset = newDataStart
            newFs.Position = 0
            Dim headerBytes2 As Byte()
            Using ms2 As New MemoryStream()
                serializer.Serialize(ms2, _header)
                headerBytes2 = ms2.ToArray()
            End Using
            Using bw As New BinaryWriter(newFs, Encoding.UTF8, leaveOpen:=True)
                bw.Write(Encoding.ASCII.GetBytes(MAGIC))
                bw.Write(VERSION)
                bw.Write(headerBytes2.Length)
                bw.Write(headerBytes2)
            End Using
            newFs.Flush(True)
        End Using

        ' 2) csere
        File.Replace(temp, _path, Nothing)

        ' 3) runtime újraépítés
        ReadHeaderXml()
        BuildRuntime()
        BuildIndexes()
    End Sub

    ' ===== Tokenizálás és szerializáció =====

    Private Function SplitTopLevel(s As String) As List(Of String)
        Dim res As New List(Of String)
        Dim depth = 0
        Dim inStr = False
        Dim quote As Char = Chr(0)
        Dim cur As New StringBuilder()
        For i = 0 To s.Length - 1
            Dim ch = s(i)
            If inStr Then
                cur.Append(ch)
                If ch = quote Then inStr = False
            Else
                If ch = "'"c OrElse ch = """"c OrElse ch = "`"c OrElse ch = "["c Then
                    inStr = True
                    quote = If(ch = "["c, "]"c, ch)
                    cur.Append(ch)
                ElseIf ch = "("c Then
                    depth += 1 : cur.Append(ch)
                ElseIf ch = ")"c Then
                    depth -= 1 : cur.Append(ch)
                ElseIf ch = ","c AndAlso depth = 0 Then
                    res.Add(cur.ToString())
                    cur.Clear()
                Else
                    cur.Append(ch)
                End If
            End If
        Next
        If cur.Length > 0 Then res.Add(cur.ToString())
        Return res
    End Function

    Private Function SplitCsvRespectQuotes(s As String) As List(Of String)
        Return SplitTopLevel(s)
    End Function

    Private Function SplitAndConditions(s As String) As List(Of String)
        Dim res As New List(Of String)
        Dim depth = 0
        Dim inStr = False
        Dim quote As Char = Chr(0)
        Dim cur As New StringBuilder()
        Dim i = 0
        While i < s.Length
            If inStr Then
                cur.Append(s(i))
                If s(i) = quote Then inStr = False
                i += 1
            Else
                If s(i) = "'"c OrElse s(i) = """"c OrElse s(i) = "`"c OrElse s(i) = "["c Then
                    inStr = True
                    quote = If(s(i) = "["c, "]"c, s(i))
                    cur.Append(s(i))
                    i += 1
                ElseIf s(i) = "("c Then
                    depth += 1 : cur.Append(s(i)) : i += 1
                ElseIf s(i) = ")"c Then
                    depth -= 1 : cur.Append(s(i)) : i += 1
                ElseIf depth = 0 AndAlso i + 3 <= s.Length AndAlso String.Compare(s, i, "AND", 0, 3, True) = 0 _
                    AndAlso (i = 0 OrElse Char.IsWhiteSpace(s(i - 1))) _
                    AndAlso (i + 3 = s.Length OrElse Char.IsWhiteSpace(s(i + 3))) Then
                    res.Add(cur.ToString())
                    cur.Clear()
                    i += 3
                Else
                    cur.Append(s(i)) : i += 1
                End If
            End If
        End While
        If cur.Length > 0 Then res.Add(cur.ToString())
        Return res
    End Function

    Private Sub ExtractFirstToken(s As String, ByRef token As String, ByRef rest As String)
        s = s.Trim()
        If s.Length = 0 Then token = Nothing : rest = "" : Return
        If s(0) = """"c OrElse s(0) = "'"c OrElse s(0) = "`"c OrElse s(0) = "["c Then
            Dim qStart = s(0)
            Dim qEnd = If(qStart = "["c, "]"c, qStart)
            Dim j = 1
            While j < s.Length AndAlso s(j) <> qEnd
                j += 1
            End While
            If j >= s.Length Then Throw New ArgumentException("Lezáratlan idéző: " & s)
            token = s.Substring(0, j + 1)
            rest = s.Substring(j + 1).TrimStart()
        Else
            Dim j = 0
            While j < s.Length AndAlso Not Char.IsWhiteSpace(s(j))
                If s(j) = "("c OrElse s(j) = ")"c OrElse s(j) = ","c Then Exit While
                j += 1
            End While
            token = s.Substring(0, j)
            rest = s.Substring(j).TrimStart()
        End If
    End Sub

    Private Function UnquoteIdentifier(s As String) As String
        s = s.Trim()
        If s.Length = 0 Then Return s
        If (s(0) = """"c AndAlso s.EndsWith("""")) OrElse
           (s(0) = "'"c AndAlso s.EndsWith("'")) OrElse
           (s(0) = "`"c AndAlso s.EndsWith("`")) OrElse
           (s(0) = "["c AndAlso s.EndsWith("]")) Then
            Return s.Substring(1, s.Length - 2)
        End If
        Return s
    End Function

    Private Function ParseLiteral(token As String) As Object
        token = token.Trim()
        If (token.StartsWith("'") AndAlso token.EndsWith("'")) OrElse
           (token.StartsWith("""") AndAlso token.EndsWith("""")) Then
            Dim inner = token.Substring(1, token.Length - 2)
            inner = inner.Replace("''", "'")
            Return inner
        End If

        If token.Equals("CURRENT_TIMESTAMP", StringComparison.OrdinalIgnoreCase) OrElse
           token.Equals("(datetime(current_timestamp,'localtime'))", StringComparison.OrdinalIgnoreCase) Then
            Return DateTime.Now
        End If

        Dim i As Integer
        If Integer.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, i) Then Return i
        Dim l As Long
        If Long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, l) Then Return CLng(l)
        Dim d As Double
        If Double.TryParse(token, NumberStyles.Float Or NumberStyles.AllowThousands, CultureInfo.InvariantCulture, d) Then Return d

        If token.Equals("1") Then Return True
        If token.Equals("0") Then Return False
        If token.Equals("TRUE", StringComparison.OrdinalIgnoreCase) Then Return True
        If token.Equals("FALSE", StringComparison.OrdinalIgnoreCase) Then Return False

        Return token
    End Function

    Private Sub ExtractDefaultExpression(s As String, ByRef defaultExpr As String, ByRef after As String)
        Dim m = Regex.Match(s, "DEFAULT\s+(?<val>.+)$", RegexOptions.IgnoreCase)
        If Not m.Success Then defaultExpr = Nothing : after = s : Return
        defaultExpr = m.Groups("val").Value.Trim()
        after = ""
    End Sub

    Private Function MapSqlTypeToInternal(sqlType As String) As String
        Dim t = sqlType.Trim().ToLowerInvariant()
        If t.StartsWith("varchar") OrElse t.StartsWith("text") OrElse t.StartsWith("nvarchar") Then Return "String"
        If t.StartsWith("int(") OrElse t = "int" OrElse t.StartsWith("integer") Then Return "Int32"
        If t = "bigint" Then Return "Int64"
        If t = "double" OrElse t = "real" OrElse t = "float" Then Return "Double"
        If t = "bit" OrElse t = "boolean" OrElse t = "bool" Then Return "Boolean"
        If t = "datetime" OrElse t = "date" OrElse t = "timestamp" Then Return "DateTime"
        If t = "blob" OrElse t = "binary" Then Return "Blob"
        Return "String"
    End Function

    Private Function MapTypeToClr(typeName As String) As Type
        Select Case typeName
            Case "String" : Return GetType(String)
            Case "Int32" : Return GetType(Integer)
            Case "Int64" : Return GetType(Long)
            Case "Double" : Return GetType(Double)
            Case "Boolean" : Return GetType(Boolean)
            Case "DateTime" : Return GetType(DateTime)
            Case "Blob" : Return GetType(Byte())
            Case Else : Return GetType(String)
        End Select
    End Function

    Private Function NormalizeType(value As Object, typeName As String, allowNull As Boolean) As Object
        If value Is Nothing Then
            If allowNull Then Return Nothing Else Throw New ArgumentNullException()
        End If
        Select Case typeName
            Case "String" : Return Convert.ToString(value, CultureInfo.InvariantCulture)
            Case "Int32" : Return Convert.ToInt32(value, CultureInfo.InvariantCulture)
            Case "Int64" : Return Convert.ToInt64(value, CultureInfo.InvariantCulture)
            Case "Double"
                If TypeOf value Is String Then
                    Return Double.Parse(DirectCast(value, String), CultureInfo.InvariantCulture)
                End If
                Return Convert.ToDouble(value, CultureInfo.InvariantCulture)
            Case "Boolean"
                If TypeOf value Is String Then
                    Dim s = DirectCast(value, String)
                    If String.Equals(s, "1") OrElse String.Equals(s, "true", StringComparison.OrdinalIgnoreCase) Then Return True
                    If String.Equals(s, "0") OrElse String.Equals(s, "false", StringComparison.OrdinalIgnoreCase) Then Return False
                End If
                Return Convert.ToBoolean(value, CultureInfo.InvariantCulture)
            Case "DateTime"
                If TypeOf value Is DateTime Then Return DirectCast(value, DateTime)
                If TypeOf value Is String Then
                    Dim dt As DateTime
                    If DateTime.TryParse(DirectCast(value, String), CultureInfo.InvariantCulture, DateTimeStyles.None, dt) Then Return dt
                    If String.Equals(DirectCast(value, String), "CURRENT_TIMESTAMP", StringComparison.OrdinalIgnoreCase) Then Return DateTime.Now
                End If
                If TypeOf value Is Long Then Return New DateTime(CLng(value))
                Throw New ArgumentException("Nem konvertálható DateTime típusra.")
            Case "Blob"
                If TypeOf value Is Byte() Then Return DirectCast(value, Byte())
                Throw New ArgumentException("Blob mezőhöz Byte() szükséges.")
            Case Else
                Return Convert.ToString(value, CultureInfo.InvariantCulture)
        End Select
    End Function

    Private Function TryParseDefault(c As ColumnDef, ByRef value As Object) As Boolean
        If String.IsNullOrWhiteSpace(c.DefaultValue) Then value = Nothing : Return False
        Dim dv = c.DefaultValue.Trim()

        If dv.Equals("CURRENT_TIMESTAMP", StringComparison.OrdinalIgnoreCase) OrElse
           dv.Equals("(datetime(current_timestamp,'localtime'))", StringComparison.OrdinalIgnoreCase) Then
            value = DateTime.Now : Return True
        End If

        If (dv.StartsWith("'") AndAlso dv.EndsWith("'")) OrElse (dv.StartsWith("""") AndAlso dv.EndsWith("""")) Then
            value = dv.Substring(1, dv.Length - 2).Replace("''", "'")
            Return True
        End If

        Dim i As Integer
        If Integer.TryParse(dv, NumberStyles.Integer, CultureInfo.InvariantCulture, i) Then
            If c.Type = "Int64" Then value = CLng(i) : Return True
            If c.Type = "Double" Then value = CDbl(i) : Return True
            If c.Type = "Boolean" Then value = (i <> 0) : Return True
            value = i : Return True
        End If
        Dim l As Long
        If Long.TryParse(dv, NumberStyles.Integer, CultureInfo.InvariantCulture, l) Then
            If c.Type = "Double" Then value = CDbl(l) : Return True
            If c.Type = "Boolean" Then value = (l <> 0) : Return True
            value = l : Return True
        End If
        Dim d As Double
        If Double.TryParse(dv, NumberStyles.Float Or NumberStyles.AllowThousands, CultureInfo.InvariantCulture, d) Then
            If c.Type = "Boolean" Then value = (d <> 0) : Return True
            value = d : Return True
        End If

        If dv.Equals("TRUE", StringComparison.OrdinalIgnoreCase) Then value = True : Return True
        If dv.Equals("FALSE", StringComparison.OrdinalIgnoreCase) Then value = False : Return True

        value = dv : Return True
    End Function

    Private Function SerializeRow(def As TableDef, values As Object()) As Byte()
        Using ms As New MemoryStream()
            Using bw As New BinaryWriter(ms, Encoding.UTF8, leaveOpen:=True)
                Dim n = def.Columns.Count
                Dim nbLen = CInt(Math.Ceiling(n / 8.0))
                Dim nb(nbLen - 1) As Byte
                For i = 0 To n - 1
                    If values(i) Is Nothing Then
                        nb(i \ 8) = CByte(nb(i \ 8) Or (1 << (i Mod 8)))
                    End If
                Next
                bw.Write(nb)

                For i = 0 To n - 1
                    Dim col = def.Columns(i)
                    Dim v = values(i)
                    If v Is Nothing Then Continue For
                    Select Case col.Type
                        Case "String"
                            Dim s = CStr(v)
                            Dim bytes = Encoding.UTF8.GetBytes(s)
                            bw.Write(bytes.Length)
                            bw.Write(bytes)
                        Case "Int32" : bw.Write(CInt(v))
                        Case "Int64" : bw.Write(CLng(v))
                        Case "Double" : bw.Write(BitConverter.DoubleToInt64Bits(CDbl(v)))
                        Case "Boolean" : bw.Write(CByte(If(CBool(v), 1, 0)))
                        Case "DateTime" : bw.Write(CType(v, DateTime).Ticks)
                        Case "Blob"
                            Dim b = DirectCast(v, Byte())
                            bw.Write(b.Length)
                            bw.Write(b)
                        Case Else
                            Dim s = Convert.ToString(v)
                            Dim bytes = Encoding.UTF8.GetBytes(s)
                            bw.Write(bytes.Length)
                            bw.Write(bytes)
                    End Select
                Next
            End Using
            Return ms.ToArray()
        End Using
    End Function

    Private Function DeserializeRow(def As TableDef, payload() As Byte) As Object()
        Dim values(def.Columns.Count - 1) As Object
        Using ms As New MemoryStream(payload)
            Using br As New BinaryReader(ms, Encoding.UTF8, leaveOpen:=True)
                Dim n = def.Columns.Count
                Dim nbLen = CInt(Math.Ceiling(n / 8.0))
                Dim nb = br.ReadBytes(nbLen)
                For i = 0 To n - 1
                    Dim isNull = ((nb(i \ 8) >> (i Mod 8)) And 1) = 1
                    If isNull Then
                        values(i) = Nothing
                    Else
                        Dim col = def.Columns(i)
                        Select Case col.Type
                            Case "String"
                                Dim len = br.ReadInt32()
                                Dim bytes = br.ReadBytes(len)
                                values(i) = Encoding.UTF8.GetString(bytes)
                            Case "Int32" : values(i) = br.ReadInt32()
                            Case "Int64" : values(i) = br.ReadInt64()
                            Case "Double"
                                Dim bits = br.ReadInt64()
                                values(i) = BitConverter.Int64BitsToDouble(bits)
                            Case "Boolean" : values(i) = (br.ReadByte() = 1)
                            Case "DateTime"
                                Dim ticks = br.ReadInt64()
                                values(i) = New DateTime(ticks)
                            Case "Blob"
                                Dim len = br.ReadInt32()
                                values(i) = br.ReadBytes(len)
                            Case Else
                                Dim len = br.ReadInt32()
                                Dim bytes = br.ReadBytes(len)
                                values(i) = Encoding.UTF8.GetString(bytes)
                        End Select
                    End If
                Next
            End Using
        End Using
        Return values
    End Function
End Class