namespace StellarNet.Lite.Client.Core
{
    /// <summary>
    /// 客户端房间组件基类 (ClientRoomComponent)
    /// 职责：定义客户端房间表现与状态同步模块的生命周期。
    /// </summary>
    public abstract class ClientRoomComponent
    {
        public ClientRoom Room { get; internal set; }

        public virtual void OnInit()
        {
        }

        public virtual void OnDestroy()
        {
        }
    }
}