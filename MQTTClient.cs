using System.Text.Json;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;

namespace ecoflow_ble;

public class MQTTClient: IDisposable
{
    private readonly IMqttClient _client;
    private readonly IConfiguration _config;
    private readonly ILogger<MQTTClient> _logger;

    public MQTTClient(IConfiguration config, ILogger<MQTTClient> logger)
    {
        _config = config;
        _logger = logger;
        _client = new MqttFactory().CreateMqttClient();
    }

    public async Task Connect()
    {
        var server = _config["MqttServer"];
        var port = _config.GetValue<int>("MqttPort");
        _logger.LogInformation("Connecting to MQTT broker at {Server}:{Port}", server, port);
        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(server, port)
            .WithCredentials(_config["MqttUsername"], _config["MqttPassword"])
            .WithClientId("ecoflow")
            .Build();

        await _client.ConnectAsync(options);
    }

    public async Task Send(byte battery, short inPower, short outPower, byte temp, CancellationToken token = default)
    {
        var msg = new MqttApplicationMessageBuilder()
            .WithTopic("ecoflow")
            .WithPayload(JsonSerializer.Serialize(new { battery, inPower, outPower, temp }))
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.ExactlyOnce)
            .Build();
        await _client.PublishAsync(msg, token);
    }

    public void Dispose()
    {
        _logger.LogInformation("Disconnecting MQTT client");
        _client.DisconnectAsync().GetAwaiter().GetResult();
        _logger.LogInformation("Disposing MQTT client");
        _client.Dispose();
    }
}