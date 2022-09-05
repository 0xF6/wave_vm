namespace ishtar;

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.Arm;
using Iced.Intel;
using static Iced.Intel.AssemblerRegisters;

public unsafe static class IshtarJIT
{
    private static readonly CallFrame JITFrame = IshtarFrames.Jit();
    public static Architecture Architecture => RuntimeInformation.ProcessArchitecture;
    private static bool IsWindow => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    private static void* _processHandle;
    public static void* ProcessHandle
        => _processHandle == null ?
           _processHandle = Process.GetCurrentProcess().Handle.ToPointer() :
           _processHandle;

    // x86 is not support, but need safe apply arm32
    public static Assembler AllocEmitter()
        => new Assembler(64);

    public static void* WrapNativeCall(void* procedureHandle)
        => WrapNativeCall(new IntPtr(procedureHandle)).ToPointer();

    
    public static IntPtr WrapNativeCall(IntPtr procedureHandle)
    {
        if (Architecture is Architecture.Arm64 or Architecture.Arm)
            throw new NotSupportedException("Arm32/64 not support");
        if (!Environment.Is64BitProcess)
            throw new NotSupportedException("x86 not support");
        var c = AllocEmitter();
        
        //c.test(ecx, ecx);
        //c.push((uint)procedureHandle.ToInt32());

        c.test(r11, r11);
        // register spillage
        c.push(rbp);
        c.sub(rsp, 0x20);
        c.lea(rbp, __[rsp+0x20]);
        c.mov(r11, procedureHandle.ToInt64());
        c.call(r11);
        c.add(rsp, 0x20);
        c.pop(rbp);
        c.ret();

        return new IntPtr(RecollectExecuteMemory(c));
    }
    public static void* WrapNativeCall(void* procedureHandle, void** retMemory)
        => WrapNativeCall(new IntPtr(procedureHandle), new IntPtr(retMemory)).ToPointer();

    public static IntPtr WrapNativeCall(IntPtr procedureHandle, IntPtr retMemory)
    {
        if (Architecture is Architecture.Arm64 or Architecture.Arm)
            throw new NotSupportedException("Arm32/64 not support");
        if (!Environment.Is64BitProcess)
            throw new NotSupportedException("x86 not support");
        var c = AllocEmitter();

        c.push(rbp);
        c.sub(rsp, 0x20);
        c.lea(rbp, __[rsp+0x20]);
        c.mov(r11, procedureHandle.ToInt64());
        c.call(r11);
        c.mov(rdx, retMemory.ToInt64());
        c.mov(__[rdx], rax);
        c.add(rsp, 0x20);
        c.pop(rbp);
        
        c.ret();

        return new IntPtr(RecollectExecuteMemory(c));
    }

    private struct ArgConventions
    {
        private readonly void* _argsMemory;
        private byte _offset;
        private static readonly AssemblerRegister64[] decl = new []
        {
            rdi,
            rsi,
            rdx,
            r10,
            r8,
            r9
        };

        public ArgConventions(void* argsMemory) => _argsMemory = argsMemory;


        public AssemblerRegister64 pop(out byte offset)
        {
            var o = _offset++;
            offset = (byte)(0x10 + (o * 8));
            return decl[o];
        }
    }

    public class NativeCallInfo
    {
        public bool retIsPointer { get; set; }
    }
    
    public static void* WrapNativeCall(void* procedure, void* returnMemory, void* argsMemory)
    {
        var c = AllocEmitter();
        var handle = new IntPtr(procedure).ToInt64();
        
        //c.push(rbp);
        // register spillage
        //c.sub(rsp, 0x28);
        //c.lea(rbp, __[rsp+0x28]);

        c.test(r11, r11);
        c.mov(r11, handle);
        c.push(rbp);
        c.mov(rbp, rsp);
        c.sub(rsp, 16);

        if (argsMemory is not null)
        {
            /*c.xor(eax, eax);
            c.mov(rbp, rsp);
            c.mov(rdi, new IntPtr(argsMemory).ToInt64());
            c.mov(eax, 0x0);*/
            //c.test(al, al);
            //c.mov(__[rsp+0x28], rsi);
            // test 2
            /*c.test(r10, r10);
            c.xor(edx, edx);
            c.mov(r10, new IntPtr(argsMemory).ToInt64());
            c.call(r10);
            c.mov(rcx, __[rax+8]);
            c.mov(ecx, __[rcx]);*/
            c.mov(rax, new IntPtr(argsMemory).ToInt64());
            c.mov(edi, __[rax]);
        }
        
        c.call(r11);
        c.mov(__[rbp - 4], eax);
        c.lea(rax, __[rbp - 4]);
        c.mov(__[new IntPtr(returnMemory).ToInt64()], rax);
        c.add(rsp, 16);
        c.pop(rbp);
        c.ret();

        //c.mov(rdx, __[rax + 8]);
        //c.mov(edx, __[rdx]);
        //c.mov(__[rsp+0x24], edx);
        //c.lea(rdx, __[rsp+0x24]);
        //c.mov(__[rax+0x10], rdx);
        //c.add(rsp, 0x28);
        //c.ret();

        //if (returnMemory != null)
        //{
        //    c.mov(rdx, ((IntPtr)returnMemory).ToInt64());
        //    c.mov(__[rdx], rax);
        //}
        //else
        //    c.nop();

        //c.add(rsp, 0x28); // cleanup
        //c.pop(rbp);
        //c.ret();

        return RecollectExecuteMemory(c);
    }




    private static void* RecollectExecuteMemory(Assembler asm)
    {
        using var stream = new MemoryStream();
        var r = asm.Assemble(new StreamCodeWriter(stream), 0);
        var asm_code = stream.ToArray();
        var asm_size = (uint)asm_code.Length;
        void* asm_mem = NativeApi.VirtualAlloc(null, asm_size,  NativeApi.AllocationType.Commit,  NativeApi.MemoryProtection.ReadWrite);
        Marshal.Copy(asm_code, 0, new IntPtr(asm_mem), asm_code.Length);
        FlushInstructions(asm_mem, asm_size);
        var isProtected = NativeApi.VirtualProtect(asm_mem, asm_size, NativeApi.Protection.PAGE_EXECUTE_READ, out _);
        if (!isProtected)
        {
            VM.FastFail(WNE.STATE_CORRUPT, "virtual protect failed set PAGE_EXECUTE_READ", JITFrame);
            return null;
        }
        return asm_mem; //(delegate*<void>)asm_mem;
    }

    public static void FlushInstructions(void* ipBaseAddr, uint size)
        => NativeApi.FlushInstructionCache(ProcessHandle, ipBaseAddr, size);
}


/*
     *    0x0000000000400526: push   rbp
       0x0000000000400527: mov    rbp,rsp         # stack-frame boilerplate
       0x000000000040052a: mov    edi,0x4005c4    # first arg
       0x000000000040052f: mov    eax,0x0         # 0 FP args in vector registers
     */
/* void* 
             *
    L000a: xor eax, eax
    L000c: mov [rbp-8], rax
    L0010: mov [rbp+0x10], rcx
    L0014: mov [rbp+0x18], rdx
    L0018: mov rcx, 0x7ffca892cb00
    L0022: xor edx, edx
             */
/* void* 
 *
L000a: xor eax, eax
L000c: mov [rbp-8], rax
L0010: mov [rbp+0x10], rcx
L0014: mov [rbp+0x18], rdx
L0018: mov [rbp+0x20], r8
L001c: mov rcx, 0x7ffca89dcb00
L0026: xor edx, edx
 */

//c.push(rbp);
//c.sub(rsp, 0x20);
//c.lea(rbp, __[rsp+0x20]);
//c.mov(r11, procedureHandle.ToInt64());
//c.jmp(r11);

//c.nop();
//c.mov(__[retMemory.ToInt64()], rax);
//c.pop(rbp);

/*C.ret()
    L000a: call 0x00007ffca8030460
    L000f: mov rdx, 0x432322245
    L0019: mov [rdx], rax
    L001c: add rsp, 0x20
    L0020: pop rbp
    L0021: ret
*/

/*
C.ret(Int32)
    L000a: mov [rbp+0x10], ecx
    L000d: mov ecx, [rbp+0x10]

    L0010: call 0x00007ffca8040460
    L0015: mov rdx, 0x432322245
    L001f: mov [rdx], rax
    L0022: add rsp, 0x20
    L0026: pop rbp
    L0027: ret*/

/*C.ret(Int32, Int32)
    L000a: mov [rbp+0x10], ecx
    L000d: mov [rbp+0x18], edx
    L0010: mov ecx, [rbp+0x10]
    L0013: mov edx, [rbp+0x18]
    L0016: call 0x00007ffca8060460
    L001b: mov rdx, 0x432322245
    L0025: mov [rdx], rax
    L0028: add rsp, 0x20
    L002c: pop rbp
    L002d: ret
*/