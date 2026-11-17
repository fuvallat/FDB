Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text

Public Class Form1
    Private Sub Form1_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        '' Inicializálás (új fájl esetén metaadatokkal)
        'Dim db = New MiniDb("adatbazis.fdb1", databaseName:="HulladékNyilvántartás", version:="1.0", status:="Aktív")

        '' Metaadat lekérdezés
        'Dim meta = db.GetMeta()
        'Console.WriteLine($"{meta.DatabaseName} | {meta.Created} | {meta.Version} | {meta.Status}")

        '' Tábla létrehozása SQLite-szerű definícióból (idézőzött nevekkel és defaulttal)
        'db.ExecuteQuery("CREATE TABLE IF NOT EXISTS Beállítások (" &
        '        "Felhasználó varchar PRIMARY KEY, " &
        '        "PozícióX int, " &
        '        "PozícióY int, " &
        '        """Teljes méret"" varchar, " &
        '        "Lezárás bit DEFAULT 1, " &
        '        "Késleltetés integer DEFAULT 15)")

        '' Beszúrás
        'db.ExecuteQuery("INSERT INTO Beállítások (Felhasználó, PozícióX, PozícióY, ""Teljes méret"") " &
        '        "VALUES ('robert', 100, 200, 'maximalis')")

        '' Lekérdezés
        'Dim rows = db.ExecuteQuery("SELECT Felhasználó, ""Teljes méret"" FROM Beállítások WHERE Felhasználó = 'robert'")
        'For Each r In rows
        '    Console.WriteLine($"{r("Felhasználó")} | {r("Teljes méret")}")
        'Next

        '' DataGridView feltöltés (WinForms példában):
        '' DataGridView1.DataSource = db.ToDataTable("Beállítások")


        '' Adatbázis példány létrehozása
        'Dim db As New MiniDb("teszt.fdb1", "TesztDB", "1.0", "Active")

        '' Triggerek feliratkozása
        'AddHandler db.BeforeInsert, Sub(tbl, vals)
        '                                Console.WriteLine($"[BEFORE INSERT] {tbl} -> {String.Join(", ", vals.Select(Function(kv) kv.Key & "=" & kv.Value))}")
        '                            End Sub
        'AddHandler db.AfterInsert, Sub(tbl, vals)
        '                               Console.WriteLine($"[AFTER INSERT] {tbl} -> {String.Join(", ", vals.Select(Function(kv) kv.Key & "=" & kv.Value))}")
        '                           End Sub
        'AddHandler db.BeforeUpdate, Sub(tbl, oldVals, newVals)
        '                                Console.WriteLine($"[BEFORE UPDATE] {tbl} -> {oldVals("Name")} -> {newVals("Name")}")
        '                            End Sub
        'AddHandler db.AfterUpdate, Sub(tbl, oldVals, newVals)
        '                               Console.WriteLine($"[AFTER UPDATE] {tbl} -> {oldVals("Name")} -> {newVals("Name")}")
        '                           End Sub
        'AddHandler db.BeforeDelete, Sub(tbl, vals)
        '                                Console.WriteLine($"[BEFORE DELETE] {tbl} -> {vals("Name")}")
        '                            End Sub
        'AddHandler db.AfterDelete, Sub(tbl, vals)
        '                               Console.WriteLine($"[AFTER DELETE] {tbl} -> {vals("Name")}")
        '                           End Sub

        '' Tábla létrehozása (ha még nincs)
        'Try
        '    db.ExecuteQuery("
        '        CREATE TABLE IF NOT EXISTS People (
        '            Id INT NOT NULL,
        '            Name TEXT NOT NULL,
        '            Age INT,
        '            PRIMARY KEY(Id),
        '            UNIQUE INDEX(Name)
        '        )
        '    ")
        'Catch ex As Exception
        '    Console.WriteLine("Tábla létrehozás hiba: " & ex.Message)
        'End Try

        '' --- Tranzakció teszt ---
        'Console.WriteLine(vbCrLf & "=== TRANZAKCIÓ TESZT ===")
        'db.BeginTransaction()
        'db.ExecuteQuery("INSERT INTO People (Id, Name, Age) VALUES (1, 'Alice', 30)")
        'db.ExecuteQuery("INSERT INTO People (Id, Name, Age) VALUES (2, 'Bob', 25)")
        'db.Commit() ' Mindkettő egyszerre kerül be

        '' --- Rollback teszt ---
        'Console.WriteLine(vbCrLf & "=== ROLLBACK TESZT ===")
        'db.BeginTransaction()
        'db.ExecuteQuery("INSERT INTO People (Id, Name, Age) VALUES (3, 'Charlie', 40)")
        'db.ExecuteQuery("UPDATE People SET Name = 'Bobby' WHERE Id = 2")
        'db.Rollback() ' Semmi nem kerül be ebből

        '' --- Normál írás (nem tranzakciós) ---
        'Console.WriteLine(vbCrLf & "=== NORMÁL ÍRÁS TESZT ===")
        'db.ExecuteQuery("INSERT INTO People (Id, Name, Age) VALUES (4, 'Diana', 28)")

        '' --- Törlés teszt ---
        'Console.WriteLine(vbCrLf & "=== DELETE TESZT ===")
        'db.ExecuteQuery("DELETE FROM People WHERE Id = 1")

        '' --- Reload teszt ---
        'Console.WriteLine(vbCrLf & "=== RELOAD TESZT ===")
        'db.Reload()

        '' --- Adatok listázása ---
        'Console.WriteLine(vbCrLf & "=== ADATOK ===")
        'Dim rows = db.ExecuteQuery("SELECT * FROM People ORDER BY Id")
        'For Each row In rows
        '    Console.WriteLine(String.Join(", ", row.Select(Function(kv) kv.Key & "=" & kv.Value)))
        'Next

        'db.Dispose()


        '        Dim db As New MiniDb("stresszteszt.fdb1", "StresszDB", "1.0", "Active")

        '        ' --- Triggerek ---
        '        AddHandler db.BeforeInsert, Sub(tbl, vals)
        '                                        Console.WriteLine($"[BEFORE INSERT] {tbl} -> {String.Join(", ", vals.Select(Function(kv) kv.Key & "=" & kv.Value))}")
        '                                    End Sub
        '        AddHandler db.AfterInsert, Sub(tbl, vals)
        '                                       Console.WriteLine($"[AFTER INSERT]  {tbl} -> {String.Join(", ", vals.Select(Function(kv) kv.Key & "=" & kv.Value))}")
        '                                   End Sub
        '        AddHandler db.BeforeUpdate, Sub(tbl, oldVals, newVals)
        '                                        Console.WriteLine($"[BEFORE UPDATE] {tbl} -> {oldVals("Name")} -> {newVals("Name")}")
        '                                    End Sub
        '        AddHandler db.AfterUpdate, Sub(tbl, oldVals, newVals)
        '                                       Console.WriteLine($"[AFTER UPDATE]  {tbl} -> {oldVals("Name")} -> {newVals("Name")}")
        '                                   End Sub
        '        AddHandler db.BeforeDelete, Sub(tbl, vals)
        '                                        Console.WriteLine($"[BEFORE DELETE] {tbl} -> {vals("Name")}")
        '                                    End Sub
        '        AddHandler db.AfterDelete, Sub(tbl, vals)
        '                                       Console.WriteLine($"[AFTER DELETE]  {tbl} -> {vals("Name")}")
        '                                   End Sub

        '        ' --- Több soros CREATE TABLE ---
        '        db.ExecuteQuery(
        '"CREATE TABLE IF NOT EXISTS People (
        '    Id INT NOT NULL,
        '    Name TEXT NOT NULL,
        '    Age INT,
        '    Note TEXT,
        '    PRIMARY KEY(Id),
        '    UNIQUE INDEX(Name)
        ')"
        '        )

        '        ' --- Tranzakció + commit ---
        '        Console.WriteLine(vbCrLf & "=== TRANZAKCIÓ + COMMIT ===")
        '        db.BeginTransaction()
        '        db.ExecuteQuery(
        '"INSERT INTO People (Id, Name, Age, Note) VALUES (
        '    1,
        '    'Alice',
        '    30,
        '    'Első sor
        'Második sor'
        ')"
        '        )
        '        db.ExecuteQuery("INSERT INTO People (Id, Name, Age) VALUES (2, 'Bob', 25)")
        '        db.Commit()

        '        ' --- Tranzakció + rollback ---
        '        Console.WriteLine(vbCrLf & "=== TRANZAKCIÓ + ROLLBACK ===")
        '        db.BeginTransaction()
        '        db.ExecuteQuery("INSERT INTO People (Id, Name, Age) VALUES (3, 'Charlie', 40)")
        '        db.ExecuteQuery("UPDATE People SET Name = 'Bobby' WHERE Id = 2")
        '        db.Rollback()

        '        ' --- Normál írás ---
        '        Console.WriteLine(vbCrLf & "=== NORMÁL ÍRÁS ===")
        '        db.ExecuteQuery("INSERT INTO People (Id, Name, Age) VALUES (4, 'Diana', 28)")

        '        ' --- Update ---
        '        Console.WriteLine(vbCrLf & "=== UPDATE TESZT ===")
        '        db.ExecuteQuery("UPDATE People SET Age = 31 WHERE Id = 1")

        '        ' --- Delete ---
        '        Console.WriteLine(vbCrLf & "=== DELETE TESZT ===")
        '        db.ExecuteQuery("DELETE FROM People WHERE Id = 1")

        '        ' --- Reload ---
        '        Console.WriteLine(vbCrLf & "=== RELOAD TESZT ===")
        '        db.Reload()

        '        ' --- ORDER BY + LIMIT ---
        '        Console.WriteLine(vbCrLf & "=== ORDER BY + LIMIT TESZT ===")
        '        Dim limited = db.ExecuteQuery("SELECT * FROM People ORDER BY Age DESC LIMIT 2")
        '        For Each row In limited
        '            Console.WriteLine(String.Join(", ", row.Select(Function(kv) kv.Key & "=" & kv.Value)))
        '        Next

        '        ' --- Végső állapot listázása ---
        '        Console.WriteLine(vbCrLf & "=== VÉGSŐ ADATOK ===")
        '        Dim allRows = db.ExecuteQuery("SELECT * FROM People ORDER BY Id")
        '        If allRows.Count = 0 Then
        '            Console.WriteLine("(nincs adat)")
        '        Else
        '            For Each row In allRows
        '                Console.WriteLine(String.Join(", ", row.Select(Function(kv) kv.Key & "=" & kv.Value)))
        '            Next
        '        End If

        '        db.Dispose()


        '        Dim db As New MiniDb("mindentteszt.fdb1", "MindenTesztDB", "1.0", "Active")

        '        ' --- Triggerek ---
        '        AddHandler db.BeforeInsert, Sub(tbl, vals)
        '                                        Console.WriteLine($"[BEFORE INSERT] {tbl} -> {String.Join(", ", vals.Select(Function(kv) kv.Key & "=" & kv.Value))}")
        '                                    End Sub
        '        AddHandler db.AfterInsert, Sub(tbl, vals)
        '                                       Console.WriteLine($"[AFTER INSERT]  {tbl} -> {String.Join(", ", vals.Select(Function(kv) kv.Key & "=" & kv.Value))}")
        '                                   End Sub
        '        AddHandler db.BeforeUpdate, Sub(tbl, oldVals, newVals)
        '                                        Console.WriteLine($"[BEFORE UPDATE] {tbl} -> {oldVals("Name")} -> {newVals("Name")}")
        '                                    End Sub
        '        AddHandler db.AfterUpdate, Sub(tbl, oldVals, newVals)
        '                                       Console.WriteLine($"[AFTER UPDATE]  {tbl} -> {oldVals("Name")} -> {newVals("Name")}")
        '                                   End Sub
        '        AddHandler db.BeforeDelete, Sub(tbl, vals)
        '                                        Console.WriteLine($"[BEFORE DELETE] {tbl} -> {vals("Name")}")
        '                                    End Sub
        '        AddHandler db.AfterDelete, Sub(tbl, vals)
        '                                       Console.WriteLine($"[AFTER DELETE]  {tbl} -> {vals("Name")}")
        '                                   End Sub

        '        ' --- Több soros CREATE TABLE ---
        '        db.ExecuteQuery(
        '"CREATE TABLE IF NOT EXISTS People (
        '    Id INT NOT NULL,
        '    Name TEXT NOT NULL,
        '    Age INT,
        '    Note TEXT,
        '    PRIMARY KEY(Id),
        '    UNIQUE INDEX(Name)
        ')"
        '        )

        '        ' TRANZAKCIÓ + COMMIT
        '        db.BeginTransaction()
        '        db.ExecuteQuery("INSERT INTO People (Id, Name, Age) VALUES (1, 'Alice', 30)")
        '        db.ExecuteQuery("INSERT INTO People (Id, Name, Age) VALUES (2, 'Bob', 25)")
        '        db.Commit()

        '        ' TRANZAKCIÓ + ROLLBACK – csak új rekord, nincs update commitált sorra
        '        db.BeginTransaction()
        '        db.ExecuteQuery("INSERT INTO People (Id, Name, Age) VALUES (3, 'Charlie', 40)")
        '        db.Rollback()

        '        ' Normál írás – Diana bekerül és commitálódik
        '        db.ExecuteQuery("INSERT INTO People (Id, Name, Age) VALUES (4, 'Diana', 28)")

        '        ' Update – módosítsuk Dianát
        '        db.ExecuteQuery("UPDATE People SET Age = 29 WHERE Id = 4")

        '        ' Delete – töröljük csak Alice-t
        '        db.ExecuteQuery("DELETE FROM People WHERE Id = 1")

        '        ' Ellenőrzés Reload előtt
        '        Console.WriteLine("Reload előtt:")
        '        For Each row In db.ExecuteQuery("SELECT * FROM People ORDER BY Id")
        '            Console.WriteLine(String.Join(", ", row.Select(Function(kv) kv.Key & "=" & kv.Value)))
        '        Next

        '        Console.WriteLine("RAW dump (Reload előtt):")
        '        db.DumpRaw()

        '        ' Reload
        '        db.Reload()

        '        ' Ellenőrzés Reload után
        '        Console.WriteLine("Reload után:")
        '        For Each row In db.ExecuteQuery("SELECT * FROM People ORDER BY Id")
        '            Console.WriteLine(String.Join(", ", row.Select(Function(kv) kv.Key & "=" & kv.Value)))
        '        Next

        '        ' --- ORDER BY + LIMIT ---
        '        Console.WriteLine(vbCrLf & "=== ORDER BY + LIMIT TESZT ===")
        '        Dim limited = db.ExecuteQuery("SELECT * FROM People ORDER BY Age DESC LIMIT 2")
        '        For Each row In limited
        '            Console.WriteLine(String.Join(", ", row.Select(Function(kv) kv.Key & "=" & kv.Value)))
        '        Next

        '        ' --- Végső állapot listázása ---
        '        Console.WriteLine(vbCrLf & "=== VÉGSŐ ADATOK ===")
        '        Dim allRows = db.ExecuteQuery("SELECT * FROM People ORDER BY Id")
        '        If allRows.Count = 0 Then
        '            Console.WriteLine("(nincs adat)")
        '        Else
        '            For Each row In allRows
        '                Console.WriteLine(String.Join(", ", row.Select(Function(kv) kv.Key & "=" & kv.Value)))
        '            Next
        '        End If

        '        db.Dispose()

        '        CapacityTest.Main()
        Program.Main()
        '        ProgramAlter.Main()

        Dim mf = New MainForm()
        mf.Show()

        'Dim dbPath = "combined_capacity.fdb1"
        'If IO.File.Exists(dbPath) Then IO.File.Delete(dbPath)

        'Dim db As New MiniDb(dbPath, "CombinedCapacity", "1.0", "Active")
        'db.DumpRaw()
        'Dim tableCount As Integer = 0
        'Dim columnsPerTable As Integer = 5 ' pl. minden táblához 5 oszlop

        'Try
        '    Do
        '        tableCount += 1
        '        Dim tblName = $"T{tableCount}"
        '        db.ExecuteQuery($"CREATE TABLE {tblName} (Id Int32 PRIMARY KEY)")

        '        ' Hozzáadunk oszlopokat
        '        For i = 1 To columnsPerTable
        '            db.ExecuteQuery($"ALTER TABLE {tblName} ADD COLUMN Col_{i} String DEFAULT 'x'")
        '        Next

        '        ' Fejléc méret mérése
        '        Dim headerSize = db.GetHeaderSize() ' ezt a metódust neked kell megírni, pl. a _header XML hosszából
        '        Console.WriteLine($"Táblák: {tableCount}, Oszlop/tábla: {columnsPerTable}, Fejléc méret: {headerSize} bájt")

        '    Loop
        'Catch ex As Exception
        '    Console.WriteLine($"Megállt a {tableCount}. táblánál: {ex.Message}")
        'End Try

        'db.Dispose()
    End Sub
End Class

Module ProgramAlter
    Sub Main()
        Dim dbPath = "alter_table_test.fdb1"
        If IO.File.Exists(dbPath) Then IO.File.Delete(dbPath)

        ' Új adatbázis létrehozása
        Dim db As New MiniDb(dbPath, "AlterTableTeszt", "1.0", "Active")

        ' 1. Létrehozunk egy egyszerű táblát
        db.ExecuteQuery("CREATE TABLE People (Id Int32 PRIMARY KEY, Name String)")

        ' 2. Hozzáadunk új oszlopokat ALTER TABLE-lel
        db.ExecuteQuery("ALTER TABLE People ADD COLUMN Age Int32 DEFAULT 0")
        db.ExecuteQuery("ALTER TABLE People ADD COLUMN ""Teljes méret"" String DEFAULT 'nincs megadva'")
        db.ExecuteQuery("ALTER TABLE People ADD COLUMN Score Int32 NOT NULL DEFAULT 100")

        ' 3. Fejléc ellenőrzése – kiírjuk a People tábla oszlopait
        '        Dim tbl = db._header.Tables.First(Function(t) t.Name = "People")
        Console.WriteLine("=== People tábla oszlopai a fejlécben ===")
        '        For Each col In tbl.Columns
        '        Console.WriteLine($"Név={col.Name}, Típus={col.Type}, Nullable={col.Nullable}, Default={col.DefaultValue}")
        '        Next

        ' 4. Beszúrunk egy rekordot, hogy lássuk a default értékek működését
        db.ExecuteQuery("INSERT INTO People (Id, Name) VALUES (1, 'Alice')")

        ' 5. Lekérdezés – a hiányzó oszlopoknál a default érték jelenik meg
        Dim rows = db.ExecuteQuery("SELECT * FROM People")
        Console.WriteLine(vbCrLf & "=== Lekérdezés eredménye ===")
        For Each row In rows
            Console.WriteLine(String.Join(" | ", row.Select(Function(kv) $"{kv.Key}={kv.Value}")))
        Next

        db.Dispose()
    End Sub
End Module

Module CapacityTest
    Sub Main()
        Dim dbPath = "capacitytest.fdb1"
        If IO.File.Exists(dbPath) Then IO.File.Delete(dbPath)

        Dim db As New MiniDb(dbPath, "CapacityTestDB", "1.0", "Active")

        ' 1. Tábla szám teszt
        Console.WriteLine("=== TÁBLA SZÁM TESZT ===")
        Dim tableCount As Integer = 0
        Try
            Do
                tableCount += 1
                db.ExecuteQuery($"CREATE TABLE IF NOT EXISTS T{tableCount} (Id INT PRIMARY KEY, Name TEXT)")
            Loop
        Catch ex As Exception
            Console.WriteLine($"Hiba {tableCount}. tábla létrehozásánál: {ex.Message}")
        End Try
        Console.WriteLine($"Max. tábla: {tableCount - 1}")

        ' 2. Oszlopszám teszt (egy új táblán)
        Console.WriteLine(vbCrLf & "=== OSZLOPSZÁM TESZT ===")
        Dim colCount As Integer = 1
        Try
            Dim cols As String = "Id INT PRIMARY KEY"
            Do
                colCount += 1
                cols &= $", C{colCount} TEXT"
                db.ExecuteQuery($"CREATE TABLE IF NOT EXISTS ColTest ({cols})")
            Loop
        Catch ex As Exception
            Console.WriteLine($"Hiba {colCount}. oszlopnál: {ex.Message}")
        End Try
        Console.WriteLine($"Max. oszlop: {colCount - 1}")

        ' 3. Rekordszám teszt (egy táblában)
        Console.WriteLine(vbCrLf & "=== REKORDSZÁM TESZT ===")
        db.ExecuteQuery("CREATE TABLE IF NOT EXISTS People (Id INT PRIMARY KEY, Name TEXT)")
        Dim sw As New Stopwatch()
        sw.Start()
        Dim recCount As Integer = 0
        Try
            Do
                recCount += 1
                db.ExecuteQuery($"INSERT INTO People (Id, Name) VALUES ({recCount}, 'Name{recCount}')")
                If recCount Mod 1000 = 0 Then
                    sw.Stop()
                    Dim size = New IO.FileInfo(dbPath).Length
                    Console.WriteLine($"Rekord: {recCount}, idő: {sw.ElapsedMilliseconds} ms, fájlméret: {size / 1024:F1} KB")
                    sw.Start()
                End If
            Loop
        Catch ex As Exception
            Console.WriteLine($"Hiba {recCount}. rekordnál: {ex.Message}")
        End Try
        Console.WriteLine($"Max. rekord: {recCount - 1}")

        db.Dispose()
    End Sub
End Module

Module Program
    Sub Main()
        Dim dbPath = "sqlite_kompat_teszt.fdb1"
        If IO.File.Exists(dbPath) Then IO.File.Delete(dbPath)

        Dim db As New MiniDb(dbPath, "SQLiteKompatTeszt", "1.0", "Active")

        ' Az összes CREATE TABLE parancs külön elemként
        Dim createSqls As New List(Of String) From {
        "CREATE TABLE IF NOT EXISTS Beállítások (Felhasználó varchar primary key, PozícióX int, PozícióY int, ""Teljes méret"" varchar, Lezárás bit default 1, Késleltetés integer default 15)"
        }

        '        createSqls.Add("create table if not exists Beállítások (Felhasználó varchar primary key, PozícióX int, PozícióY int, " & Chr(34) & "Teljes méret" & Chr(34) & " varchar, Lezárás bit default 1, Késleltetés integer default 15)")
        createSqls.Add("create table if not exists Információk (Verzió varchar primary key default '1.0', Állapot varchar default 'Próba', Dátum datetime default (datetime(current_timestamp,'localtime')), " & Chr(34) & "Regisztrált név" & Chr(34) & " varchar, " & Chr(34) & "Regisztrált e-mail" & Chr(34) & " varchar, " & Chr(34) & "Regisztrált dátum" & Chr(34) & " varchar, " & Chr(34) & "Regisztrált kód" & Chr(34) & " varchar, Kimutatás integer default 0)")
        createSqls.Add("create table if not exists Cég (Név varchar, SzékhelyI integer(4), SzékhelyT varchar, SzékhelyC varchar, SzékhelyH varchar, TelephelyI integer(4), TelephelyT varchar, TelephelyC varchar, TelephelyH varchar, Cégvezető varchar, Kapcsolattartó varchar, Telefon varchar, Fax varchar, " & Chr(34) & "E-mail" & Chr(34) & " varchar, Internet varchar, Adószám varchar, Cégjegyzékszám varchar, KSH varchar, KTJ varchar, KÜJ varchar, VAT varchar, GLN varchar, TEÁOR varchar, Számlaszám varchar, Módosítva datetime default (datetime(current_timestamp,'localtime')), Módosította varchar, Logó blob)")
        createSqls.Add("create table if not exists Hulladékok (Azonosító varchar primary key, Beérkezés varchar, " & Chr(34) & "Ügyfél azonosító" & Chr(34) & " varchar, Ügyfél varchar, " & Chr(34) & "Származási hely azonosító" & Chr(34) & " varchar, " & Chr(34) & "Származási hely" & Chr(34) & " varchar, Jármű varchar, " & Chr(34) & "Hulladék azonosító" & Chr(34) & " varchar, " & Chr(34) & "Hulladék típusa" & Chr(34) & " varchar, Telephely varchar default '', Tároló varchar default '', 'Mennyiség (kg)' integer default 0, 'Mennyiség (m3)' integer default 0, Mérlegjegy varchar, Számla varchar default '', Szállítólevél varchar default '', Kézpénzes bit default 0, Fizetve bit default 0, Kezelés varchar default 'D1', Megjegyzés varchar default '', Módosítva datetime default (datetime(current_timestamp,'localtime')), Módosította varchar, Rontott bit default 0, Befogadó bit default 0, Nyomtatva varchar default '', " & Chr(34) & "Mért tömeg (tele)" & Chr(34) & " integer default 0, " & Chr(34) & "Mért tömeg (üres)" & Chr(34) & " integer default 0, Távozás varchar default '', Módosítás varchar default '', Érkezés varchar default '', " & Chr(34) & "MOHU azonosító" & Chr(34) & " varchar default '')")
        createSqls.Add("create table if not exists Felhasználók (Azonosító varchar primary key, Név varchar, Beosztás varchar default '', Telefon varchar default '', " & Chr(34) & "E-mail" & Chr(34) & " varchar default '', Jogkör varchar, Jelszó varchar, Módosítva datetime default (datetime(current_timestamp,'localtime')), Módosította varchar)")
        createSqls.Add("create table if not exists Ügyfelek (Azonosító varchar primary key, " & Chr(34) & "Jogi személy" & Chr(34) & " bit default 1, Ügyfél varchar, Irányítószám integer(4) default 0, Település varchar default '', Cím varchar default '', Hsz varchar default '', Cégvezető varchar default '', Kapcsolattartó varchar default '', Telefon varchar default '', Fax varchar default '', " & Chr(34) & "E-mail" & Chr(34) & " varchar default '', Érvényesség varchar default '', KSH varchar default '', KTJ varchar default '', KÜJ varchar default '', VAT varchar default '', GLN varchar default '', Adószám varchar default '', Cégjegyzékszám varchar default '', " & Chr(34) & "Azonosító okmány" & Chr(34) & " varchar default '', " & Chr(34) & "Azonosító okmány száma" & Chr(34) & " varchar default '', Ügyfélcsoport varchar default 'Cégek', Kézpénzes bit default 0, Módosítva datetime default (datetime(current_timestamp,'localtime')), Módosította varchar, Aktív bit default 1, " & Chr(34) & "MOHU partner" & Chr(34) & " bit default 0)")
        createSqls.Add("create table if not exists SzármazásiHelyek (Azonosító varchar primary key, Ügyfél varchar, Irányítószám integer(4) default 0, Település varchar default '', Cím varchar default '', Hsz varchar default '', Telefon varchar default '', " & Chr(34) & "E-mail" & Chr(34) & " varchar default '', Módosítva datetime default (datetime(current_timestamp,'localtime')), Módosította varchar)")
        createSqls.Add("create table if not exists Járművek (Azonosító varchar primary key, Ügyfél varchar, Rendszám varchar, Gépjárművezető varchar default '', Telefon varchar default '', Módosítva datetime default (datetime(current_timestamp,'localtime')), Módosította varchar)")
        createSqls.Add("create table if not exists HulladékTípusok (" & Chr(34) & "EWC-kód" & Chr(34) & " varchar primary key, Egység varchar default 'kg', 'Egységár (kg)' double default 0, 'Egységár (m3)' integer default 0, Tároló varchar, Leírás varchar, Kezelés varchar, Módosítva datetime default (datetime(current_timestamp,'localtime')), Módosította varchar)")
        createSqls.Add("create table if not exists Telephelyek (Megnevezés varchar primary key, Irányítószám integer(4) default 0, Település varchar default '', Cím varchar default '', Hsz varchar default '', Telefon varchar default '', " & Chr(34) & "E-mail" & Chr(34) & " varchar default '', Telephelyvezető varchar default '', Engedély varchar default '', KTJ varchar default '', Típus varchar default '', Módosítva datetime default (datetime(current_timestamp,'localtime')), Módosította varchar, Érvényesség varchar, Hatóság varchar)")
        createSqls.Add("create table if not exists Tárolók (Megnevezés varchar primary key, Kapacitás integer default 0, Egység varchar default 'kg', Telephely varchar, Megnyitás varchar, Lezárás varchar, Lezárva bit default 0, Módosítva datetime default (datetime(current_timestamp,'localtime')), Módosította varchar)")
        createSqls.Add("create table if not exists BelépésiNapló (Időpont datetime default (datetime(current_timestamp,'localtime')), Felhasználó varchar, Honnan varchar, 'Sikeres belépés' bit default 1)")
        createSqls.Add("create table if not exists MódosításiNapló (Időpont datetime default (datetime(current_timestamp,'localtime')), Felhasználó varchar, Hol varchar, Mit varchar, Módosítás varchar)")
        createSqls.Add("create table if not exists TörlésiNapló (Időpont datetime default (datetime(current_timestamp,'localtime')), Felhasználó varchar, " & Chr(34) & "Törölt elem" & Chr(34) & " varchar, Honnan varchar)")
        createSqls.Add("create table if not exists HelyreállításiNapló (Időpont datetime default (datetime(current_timestamp,'localtime')), Művelet varchar, Felhasználó varchar)")
        createSqls.Add("create table if not exists HibaNapló (Időpont datetime default (datetime(current_timestamp,'localtime')), Felhasználó varchar, Hiba varchar, Hol varchar)")
        createSqls.Add("create table if not exists Levelek (Azonosító varchar primary key, Időpont datetime default (datetime(current_timestamp,'localtime')), Felhasználó varchar, Cím varchar, Tartalom varchar)")
        createSqls.Add("create table if not exists Zárás (Év integer(4), Hónap varchar, Módosítva varchar default (datetime(current_timestamp,'localtime')), Módosította varchar)")
        createSqls.Add("create table if not exists Sablonok (Elnevezés varchar, Ügyfélcsoport varchar, Ügyfél varchar, " & Chr(34) & "Származási hely" & Chr(34) & " varchar, Jármű varchar, " & Chr(34) & "Hulladék típusa" & Chr(34) & " varchar, Telephely varchar, Tároló varchar, Kezelés varchar, Kézpénzes bit, Megjegyzés varchar, Módosítva varchar default (datetime(current_timestamp,'localtime')), Módosította varchar)")

        ' Végigfutunk a parancsokon
        For Each s In createSqls
            Try
                db.ExecuteQuery(s)
                Console.WriteLine("OK: " & s.Split("("c)(0))
            Catch ex As Exception
                Console.WriteLine("HIBA: " & ex.Message)
            End Try
        Next

        ' ===== Teszt adatok beszúrása =====
        Try
            db.ExecuteQuery("INSERT INTO Beállítások (Felhasználó, PozícióX, PozícióY, ""Teljes méret"", Lezárás, Késleltetés) VALUES ('tesztuser', 100, 200, '1920x1080', 0, 10)")

            db.ExecuteQuery("INSERT INTO Információk (Verzió, Állapot, ""Regisztrált név"", ""Regisztrált e-mail"", ""Regisztrált dátum"", ""Regisztrált kód"", Kimutatás) VALUES ('1.0', 'Aktív', 'Teszt Elek', 'teszt@example.com', '2025-09-14', 'ABC123', 1)")

            db.ExecuteQuery("INSERT INTO Cég (Név, SzékhelyI, SzékhelyT, SzékhelyC, SzékhelyH, Cégvezető, Telefon, ""E-mail"", Internet, Adószám) VALUES ('Teszt Kft.', 8000, 'Székesfehérvár', 'Fő utca', '1.', 'Teszt Elek', '+36-30-1234567', 'info@tesztkft.hu', 'www.tesztkft.hu', '12345678-1-12')")

            db.ExecuteQuery("INSERT INTO Felhasználók (Azonosító, Név, Beosztás, Telefon, ""E-mail"", Jogkör, Jelszó) VALUES ('admin', 'Adminisztrátor', 'Rendszergazda', '+36-30-7654321', 'admin@teszt.hu', 'Admin', 'jelszo123')")

            db.ExecuteQuery("INSERT INTO Ügyfelek (Azonosító, ""Jogi személy"", Ügyfél, Irányítószám, Település, Cím, Telefon, ""E-mail"", Aktív) VALUES ('U001', 1, 'Teszt Ügyfél Kft.', 8000, 'Székesfehérvár', 'Kossuth utca 5.', '+36-22-111222', 'ugyfel@teszt.hu', 1)")

            db.ExecuteQuery("INSERT INTO HulladékTípusok (""EWC-kód"", Egység, ""Egységár (kg)"", ""Egységár (m3)"", Tároló, Leírás, Kezelés) VALUES ('20 01 01', 'kg', 25.5, 0, 'Konténer1', 'Papír és karton', 'R1')")

            db.ExecuteQuery("INSERT INTO Telephelyek (Megnevezés, Irányítószám, Település, Cím, Telefon, ""E-mail"", Telephelyvezető) VALUES ('Központi telephely', 8000, 'Székesfehérvár', 'Ipar utca 10.', '+36-22-333444', 'telephely@teszt.hu', 'Teszt Elek')")

            db.ExecuteQuery("INSERT INTO Tárolók (Megnevezés, Kapacitás, Egység, Telephely, Megnyitás) VALUES ('Konténer1', 5000, 'kg', 'Központi telephely', '2025-01-01')")

            db.ExecuteQuery("INSERT INTO BelépésiNapló (Időpont, Felhasználó, Honnan, ""Sikeres belépés"") VALUES ('2025-09-14 08:00:00', 'admin', '192.168.1.10', 1)")

            Console.WriteLine("Teszt adatok sikeresen beszúrva.")
        Catch ex As Exception
            Console.WriteLine("Teszt adatok beszúrása közben hiba: " & ex.Message)
        End Try

        db.Dispose()
    End Sub
End Module

Public Class MainForm
    Inherits Form

    Private WithEvents cboTables As New ComboBox()
    Private WithEvents txtSearch As New TextBox()
    Private WithEvents btnExport As New Button()
    Private dgv As New DataGridView()
    Private db As MiniDb
    Private currentRows As List(Of Dictionary(Of String, Object))

    Public Sub New()
        Me.Text = "MiniDb Viewer"
        Me.Width = 900
        Me.Height = 600

        ' Felső panel a vezérlőkhöz
        Dim topPanel As New FlowLayoutPanel()
        topPanel.Dock = DockStyle.Top
        topPanel.Height = 35

        cboTables.Width = 200
        txtSearch.Width = 200
        '        txtSearch.PlaceholderText = "Keresés..."
        btnExport.Text = "Export CSV"

        topPanel.Controls.Add(New Label() With {.Text = "Tábla:", .AutoSize = True, .TextAlign = Drawing.ContentAlignment.MiddleCenter})
        topPanel.Controls.Add(cboTables)
        topPanel.Controls.Add(New Label() With {.Text = "Keresés:", .AutoSize = True, .TextAlign = Drawing.ContentAlignment.MiddleCenter})
        topPanel.Controls.Add(txtSearch)
        topPanel.Controls.Add(btnExport)

        dgv.Dock = DockStyle.Fill
        dgv.ReadOnly = True
        dgv.AllowUserToAddRows = False
        dgv.AllowUserToDeleteRows = False
        dgv.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells
        dgv.SelectionMode = DataGridViewSelectionMode.FullRowSelect

        Me.Controls.Add(dgv)
        Me.Controls.Add(topPanel)
    End Sub

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)

        ' Adatbázis megnyitása
        Dim path = "sqlite_kompat_teszt.fdb1"
        db = New MiniDb(path)

        ' Tábla nevek betöltése a fejlécből
        Dim tableNames = db.GetTables.Select(Function(t) t.Name).ToList()
        cboTables.DataSource = tableNames
    End Sub

    Private Sub cboTables_SelectedIndexChanged(sender As Object, e As EventArgs) Handles cboTables.SelectedIndexChanged
        LoadTableData()
    End Sub

    Private Sub txtSearch_TextChanged(sender As Object, e As EventArgs) Handles txtSearch.TextChanged
        ApplySearchFilter()
    End Sub

    Private Sub btnExport_Click(sender As Object, e As EventArgs) Handles btnExport.Click
        If currentRows Is Nothing OrElse currentRows.Count = 0 Then
            MessageBox.Show("Nincs exportálható adat.", "Export", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If

        Using sfd As New SaveFileDialog()
            sfd.Filter = "CSV fájl|*.csv"
            sfd.FileName = cboTables.SelectedItem.ToString() & ".csv"
            If sfd.ShowDialog() = DialogResult.OK Then
                ExportToCsv(sfd.FileName)
                MessageBox.Show("Export kész: " & sfd.FileName, "Export", MessageBoxButtons.OK, MessageBoxIcon.Information)
            End If
        End Using
    End Sub

    Private Sub LoadTableData()
        Dim tableName = cboTables.SelectedItem.ToString()
        currentRows = db.ExecuteQuery($"SELECT * FROM {tableName}")
        BindData(currentRows)
    End Sub

    Private Sub BindData(rows As List(Of Dictionary(Of String, Object)))
        dgv.Columns.Clear()
        dgv.Rows.Clear()

        If rows.Count > 0 Then
            For Each colName In rows(0).Keys
                dgv.Columns.Add(colName, colName)
            Next
            For Each row In rows
                dgv.Rows.Add(row.Values.ToArray())
            Next
        End If
        If rows.Count = 0 Then
            Dim tableName = cboTables.SelectedItem.ToString()
            Dim tblDef = db.GetTables.First(Function(t) t.Name = tableName)
            dgv.Columns.Clear()
            For Each col In tblDef.Columns
                dgv.Columns.Add(col.Name, col.Name)
            Next
            Return
        End If
    End Sub

    Private Sub ApplySearchFilter()
        If currentRows Is Nothing Then Return
        Dim filterText = txtSearch.Text.Trim().ToLower()
        If filterText = "" Then
            BindData(currentRows)
        Else
            Dim filtered = currentRows.Where(Function(r) r.Values.Any(Function(v) v IsNot Nothing AndAlso v.ToString().ToLower().Contains(filterText))).ToList()
            BindData(filtered)
        End If
    End Sub

    Private Sub ExportToCsv(filePath As String)
        Using sw As New StreamWriter(filePath, False, Encoding.UTF8)
            ' Fejléc
            sw.WriteLine(String.Join(";", dgv.Columns.Cast(Of DataGridViewColumn).Select(Function(c) c.HeaderText)))
            ' Sorok
            For Each row As DataGridViewRow In dgv.Rows
                If Not row.IsNewRow Then
                    sw.WriteLine(String.Join(";", row.Cells.Cast(Of DataGridViewCell).Select(Function(c) c.Value?.ToString())))
                End If
            Next
        End Using
    End Sub
End Class