using InfluxDB.Client;
using InfluxDB.Client.Api.Domain;
using InfluxDB.Client.Writes;

namespace ecoflow_ble;

public class DbClient: IDisposable
{
    private readonly IConfiguration _config;
    private readonly ILogger<DbClient> _logger;
    private readonly InfluxDBClient _db;

    public DbClient(IConfiguration config, ILogger<DbClient> logger)
    {
        _config = config;
        _logger = logger;
        _db = new InfluxDBClient(_config["InfluxUrl"], _config["InfluxKey"]);
        _db.SetLogLevel(InfluxDB.Client.Core.LogLevel.None);
    }

    public void Write(byte battery, short inPower, short outPower, byte temp)
    {
        using var api = _db.GetWriteApi();
        var point = PointData
            .Measurement("ecoflow")
            .Field("battery", battery)
            .Field("inPower", inPower) 
            .Field("outPower", outPower) 
            .Field("temp", temp)
            .Timestamp(DateTime.UtcNow, WritePrecision.Ms); 
        api.WritePoint(point, "anv", "anv");
    }

    public void Dispose()
    {
        _logger.LogInformation("Disposing DB client");
        _db.Dispose();
    }
}