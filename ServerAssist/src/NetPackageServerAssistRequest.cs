using UnityEngine.Scripting;

namespace ServerAssistMod
{
    [Preserve]
    public class NetPackageServerAssistRequest : NetPackage
    {
        private string payload = string.Empty;

        public override NetPackageDirection PackageDirection
        {
            get { return NetPackageDirection.ToServer; }
        }

        public NetPackageServerAssistRequest Setup(string _payload)
        {
            payload = _payload ?? string.Empty;
            return this;
        }

        public override void read(PooledBinaryReader _br)
        {
            payload = _br.ReadString();
        }

        public override void write(PooledBinaryWriter _bw)
        {
            base.write(_bw);
            _bw.Write(payload ?? string.Empty);
        }

        public override void ProcessPackage(World _world, GameManager _callbacks)
        {
            ServerAssistServer.HandleRequest(Sender, payload);
        }

        public int GetLength()
        {
            return 64 + (payload != null ? payload.Length : 0) * 2;
        }
    }
}



