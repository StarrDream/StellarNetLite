namespace StellarNet.Lite.Server.Core
{
    public abstract class RoomComponent
    {
        public Room Room { get; internal set; }

        public virtual void OnInit()
        {
        }

        public virtual void OnDestroy()
        {
        }

        public virtual void OnMemberJoined(Session session)
        {
        }

        public virtual void OnMemberLeft(Session session)
        {
        }

        public virtual void OnMemberOffline(Session session)
        {
        }

        public virtual void OnMemberOnline(Session session)
        {
        }

        public virtual void OnSendSnapshot(Session session)
        {
        }
        
        public virtual void OnGameStart()
        {
        }

        public virtual void OnGameEnd()
        {
        }
    }
}