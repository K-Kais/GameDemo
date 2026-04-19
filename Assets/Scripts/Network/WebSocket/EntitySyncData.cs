using System;

namespace GameDemo.Network
{
    [Serializable]
    public sealed class EntitySyncData
    {
        public float x;
        public float y;
        public float dirX;
        public float dirY;
        public int characterIndex = -1;
        public string state = string.Empty;
        public bool attackEvent;
        public bool attackHitEvent;
        public bool respawnEvent;
        public bool skill1;
        public bool skill2;
        public float currentHp;
        public float maxHp;
    }
}
