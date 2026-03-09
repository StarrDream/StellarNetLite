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

        // 核心新增：物理连接断开时的生命周期钩子（用于处理房主掉线移交等逻辑）
        public virtual void OnMemberOffline(Session session)
        {
        }

        // 核心新增：物理连接恢复时的生命周期钩子
        public virtual void OnMemberOnline(Session session)
        {
        }

        public virtual void OnSendSnapshot(Session session)
        {
        }
    }
}