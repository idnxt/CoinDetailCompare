using Microsoft.Data.Sqlite;
using CoinDetailCompare.App.Models;
using System.IO;

namespace CoinDetailCompare.App.Services;

/// <summary>SQLite access for the catalog database (shared schema with the backend).</summary>
public class DbService
{
    private readonly string _connectionString;

    public DbService(string dbPath, string schemaPath)
    {
        _connectionString = $"Data Source={dbPath};Foreign Keys=True;";
        Init(dbPath, schemaPath);
    }

    private void Init(string dbPath, string schemaPath)
    {
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        if (File.Exists(schemaPath))
        {
            var schema = File.ReadAllText(schemaPath);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = schema;
            cmd.ExecuteNonQuery();
        }
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    public int NextCoinId()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO coin (mint_year) VALUES (NULL) RETURNING id;";
        // INTEGER columns come back as Int64 from ExecuteScalar.
        return Convert.ToInt32(cmd.ExecuteScalar()!);
    }

    public void SaveCoin(Coin c)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE coin SET
              type_id=@type_id, mint_year=@mint_year, mint=@mint, quality=@quality,
              price=@price, album_name=@album_name, page_no=@page_no, order_no=@order_no,
              obverse_photo=@obverse_photo, reverse_photo=@reverse_photo,
              obverse_heatmap=@obverse_heatmap, reverse_heatmap=@reverse_heatmap,
              obverse_layer=@obverse_layer, reverse_layer=@reverse_layer,
              obverse_vector=@obverse_vector, reverse_vector=@reverse_vector,
              is_anomaly=@is_anomaly, anomaly_desc=@anomaly_desc,
              numista_url=@numista_url, source_kind=@source_kind, etalon_id=@etalon_id
            WHERE id=@id;";
        cmd.Parameters.AddWithValue("@id", c.Id);
        cmd.Parameters.AddWithValue("@type_id", (object?)c.TypeId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@mint_year", (object?)c.MintYear ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@mint", (object?)c.Mint ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@quality", (object?)c.Quality ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@price", c.Price);
        cmd.Parameters.AddWithValue("@album_name", (object?)c.AlbumName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@page_no", (object?)c.PageNo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@order_no", (object?)c.OrderNo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@obverse_photo", (object?)c.ObversePhoto ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@reverse_photo", (object?)c.ReversePhoto ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@obverse_heatmap", (object?)c.ObverseHeatmap ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@reverse_heatmap", (object?)c.ReverseHeatmap ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@obverse_layer", (object?)c.ObverseLayer ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@reverse_layer", (object?)c.ReverseLayer ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@obverse_vector", (object?)c.ObverseVector ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@reverse_vector", (object?)c.ReverseVector ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@is_anomaly", c.IsAnomaly ? 1 : 0);
        cmd.Parameters.AddWithValue("@anomaly_desc", (object?)c.AnomalyDesc ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@numista_url", (object?)c.NumistaUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@source_kind", c.SourceKind.ToString().ToLowerInvariant());
        cmd.Parameters.AddWithValue("@etalon_id", (object?)c.EtalonId ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public Coin? GetCoin(int id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM coin WHERE id=@id;";
        cmd.Parameters.AddWithValue("@id", id);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return MapCoin(r);
    }

    public CoinType? GetCoinType(int id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM coin_type WHERE id=@id;";
        cmd.Parameters.AddWithValue("@id", id);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new CoinType
        {
            Id = r.GetInt32(r.GetOrdinal("id")),
            NumistaN = S(r, "numista_n"),
            NumistaUrl = S(r, "numista_url"),
            Name = S(r, "name"),
            Issuer = S(r, "issuer"),
            Period = S(r, "period"),
            Type = S(r, "type"),
            Years = S(r, "years"),
            Calendar = S(r, "calendar"),
            Value = S(r, "value"),
            Rate = S(r, "rate"),
            Currency = S(r, "currency"),
            Composition = S(r, "composition"),
            Mass = S(r, "mass"),
            Diameter = S(r, "diameter"),
            Thickness = S(r, "thickness"),
            Shape = S(r, "shape"),
            Technique = S(r, "technique"),
            Orientation = S(r, "orientation"),
            IssuedDate = S(r, "issued_date"),
            Links = S(r, "links"),
            ObverseText = S(r, "obverse_text"),
            ReverseText = S(r, "reverse_text"),
            RimText = S(r, "rim_text"),
        };
    }

    public List<Coin> ListAllCoins()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM coin ORDER BY album_name, page_no, order_no, id;";
        using var r = cmd.ExecuteReader();
        var list = new List<Coin>();
        while (r.Read()) list.Add(MapCoin(r));
        return list;
    }

    public List<Coin> ListByAlbum(string albumName, int pageNo)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT * FROM coin
                            WHERE album_name=@album AND page_no=@page
                            ORDER BY order_no, id;";
        cmd.Parameters.AddWithValue("@album", albumName);
        cmd.Parameters.AddWithValue("@page", pageNo);
        using var r = cmd.ExecuteReader();
        var list = new List<Coin>();
        while (r.Read()) list.Add(MapCoin(r));
        return list;
    }

    public int SaveCoinType(CoinType t)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        if (t.Id > 0)
        {
            cmd.CommandText = @"UPDATE coin_type SET numista_n=@numista_n, numista_url=@numista_url,
                name=@name, issuer=@issuer, period=@period, type=@type, years=@years,
                calendar=@calendar, value=@value, rate=@rate, currency=@currency,
                composition=@composition, mass=@mass, diameter=@diameter, thickness=@thickness,
                shape=@shape, technique=@technique, orientation=@orientation, issued_date=@issued_date,
                links=@links, obverse_text=@obverse_text, reverse_text=@reverse_text, rim_text=@rim_text
                WHERE id=@id;";
            AddTypeParams(cmd, t);
            cmd.Parameters.AddWithValue("@id", t.Id);
            cmd.ExecuteNonQuery();
            return t.Id;
        }
        cmd.CommandText = @"INSERT INTO coin_type (
            numista_n, numista_url, name, issuer, period, type, years, calendar, value, rate,
            currency, composition, mass, diameter, thickness, shape, technique, orientation,
            issued_date, links, obverse_text, reverse_text, rim_text)
            VALUES (
            @numista_n, @numista_url, @name, @issuer, @period, @type, @years, @calendar, @value, @rate,
            @currency, @composition, @mass, @diameter, @thickness, @shape, @technique, @orientation,
            @issued_date, @links, @obverse_text, @reverse_text, @rim_text)
            RETURNING id;";
        AddTypeParams(cmd, t);
        var id = Convert.ToInt32(cmd.ExecuteScalar()!);
        t.Id = id;
        return id;
    }

    private static void AddTypeParams(SqliteCommand cmd, CoinType t)
    {
        cmd.Parameters.AddWithValue("@numista_n", (object?)t.NumistaN ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@numista_url", (object?)t.NumistaUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@name", (object?)t.Name ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@issuer", (object?)t.Issuer ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@period", (object?)t.Period ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@type", (object?)t.Type ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@years", (object?)t.Years ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@calendar", (object?)t.Calendar ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@value", (object?)t.Value ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@rate", (object?)t.Rate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@currency", (object?)t.Currency ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@composition", (object?)t.Composition ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@mass", (object?)t.Mass ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@diameter", (object?)t.Diameter ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@thickness", (object?)t.Thickness ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@shape", (object?)t.Shape ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@technique", (object?)t.Technique ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@orientation", (object?)t.Orientation ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@issued_date", (object?)t.IssuedDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@links", (object?)t.Links ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@obverse_text", (object?)t.ObverseText ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@reverse_text", (object?)t.ReverseText ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@rim_text", (object?)t.RimText ?? DBNull.Value);
    }

    private static Coin MapCoin(SqliteDataReader r)
    {
        return new Coin
        {
            Id = r.GetInt32(r.GetOrdinal("id")),
            TypeId = I(r, "type_id"),
            MintYear = S(r, "mint_year"),
            Mint = S(r, "mint"),
            Quality = S(r, "quality"),
            Price = D(r, "price"),
            AlbumName = S(r, "album_name"),
            PageNo = I(r, "page_no"),
            OrderNo = I(r, "order_no"),
            ObversePhoto = S(r, "obverse_photo"),
            ReversePhoto = S(r, "reverse_photo"),
            ObverseHeatmap = S(r, "obverse_heatmap"),
            ReverseHeatmap = S(r, "reverse_heatmap"),
            ObverseLayer = S(r, "obverse_layer"),
            ReverseLayer = S(r, "reverse_layer"),
            ObverseVector = S(r, "obverse_vector"),
            ReverseVector = S(r, "reverse_vector"),
            IsAnomaly = r.GetInt32(r.GetOrdinal("is_anomaly")) == 1,
            AnomalyDesc = S(r, "anomaly_desc"),
            NumistaUrl = S(r, "numista_url"),
            SourceKind = Enum.Parse<SourceKind>(r.GetString(r.GetOrdinal("source_kind")), ignoreCase: true),
            EtalonId = I(r, "etalon_id"),
        };
    }

    // Reader helpers: resolve column name to ordinal once, then use int overloads only.
    private static string? S(SqliteDataReader r, string col)
    {
        var o = r.GetOrdinal(col);
        return r.IsDBNull(o) ? null : r.GetString(o);
    }

    private static int? I(SqliteDataReader r, string col)
    {
        var o = r.GetOrdinal(col);
        return r.IsDBNull(o) ? null : r.GetInt32(o);
    }

    private static double D(SqliteDataReader r, string col)
    {
        var o = r.GetOrdinal(col);
        return r.IsDBNull(o) ? 0 : r.GetDouble(o);
    }
}
