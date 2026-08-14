using Renci.SshNet;

namespace MacExplorer.Services;

public interface IRemoteFileService : IFileService
{
    void SetCurrentServer(string serverId);
    SftpClient? GetConnectedClient();
}
