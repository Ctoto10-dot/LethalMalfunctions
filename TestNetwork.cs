using LethalNetworkAPI;
public class TestNetwork {
    public void Test() {
        var msg = LNetworkMessage<int>.Connect("test_id");
        msg.OnServerReceived += (data, id) => { };
        msg.OnClientReceived += (data) => { };
        msg.SendServer(1);
        msg.SendClients(1);
    }
}
