namespace ishtar_test
{
    using ishtar;
    using mana.runtime;
    using NUnit.Framework;

    [TestFixture]
    public class FECallTest : IshtarTestBase
    {
        [Test]
        [Parallelizable(ParallelScope.None)]
        public void Call_FE_Console_Println()
        {
            using var ctx = CreateContext();
            ctx.OnClassBuild((x, storage) =>
            {
                var type = x.Owner.FindType("corlib%global::mana/lang/Out", true);

                storage.method = type.FindMethod("@_println");
            });

            ctx.Execute((gen, storage) =>
            {
                gen.Emit(OpCodes.LDC_STR, "foo");
                gen.Emit(OpCodes.CALL, (ManaMethod)storage.method);
                gen.Emit(OpCodes.RET);
            });
        }
    }
}
