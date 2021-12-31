namespace ishtar
{
    using System.Runtime.InteropServices;
    public unsafe struct IshtarObject
    {
        public void* clazz;
        public void** vtable;
        public GCFlags flags;

        public uint vtable_size;

        public IshtarObject** owner;
#if DEBUG
        public long __gc_id = -1;
#endif

        public RuntimeIshtarClass decodeClass()
        {
            if (clazz is null)
                return null;
            return IshtarUnsafe.AsRef<RuntimeIshtarClass>(clazz);
        }
    }


    public abstract unsafe class NIObject
    {
        protected readonly IshtarObject* __value__;
        protected NIObject(IshtarObject* obj)
        {
            this.__value__ = obj;
            this.__value__->flags |= GCFlags.IMMORTAL;
        }

        public abstract RuntimeIshtarClass Type { get; }
    }
}
