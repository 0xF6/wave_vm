namespace ishtar
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Linq.Expressions;
    using System.Text.RegularExpressions;
    using mana.ishtar.emit;
    using mana.runtime;
    using Spectre.Console;
    using mana.syntax;
    using Xunit;

    public class GeneratorContext
    {
        internal ManaModuleBuilder Module { get; set; }
        internal Dictionary<QualityTypeName, ClassBuilder> Classes { get; } = new ();
        internal DocumentDeclaration Document { get; set; }

        public List<string> Errors = new ();

        public Dictionary<ManaMethod, ManaScope> Scopes { get; } = new();

        public ManaMethod CurrentMethod { get; set; }
        public ManaScope CurrentScope { get; set; }
        
        public GeneratorContext LogError(string err, ExpressionSyntax exp)
        {
            var pos = exp.Transform.pos;
            var diff_err = exp.Transform.DiffErrorFull(Document);
            Errors.Add($"[red bold]{err.EscapeMarkup().EscapeArgumentSymbols()}[/] \n\t" +
                       $"at '[orange bold]{pos.Line} line, {pos.Column} column[/]' \n\t" +
                       $"in '[orange bold]{Document.FileEntity}[/]'."+
                       $"{diff_err}");
            return this;
        }

        public ManaClass ResolveType(TypeSyntax targetTypeTypeword) 
            => Module.FindType(targetTypeTypeword.Identifier.ExpressionString,
                Classes[CurrentMethod.Owner.FullName].Includes);

        public ManaClass ResolveScopedIdentifierType(IdentifierExpression id)
        {
            if (CurrentScope.HasVariable(id))
                return CurrentScope.variables[id];
            if (CurrentMethod.Owner.ContainsField(id))
                return CurrentMethod.Owner.ResolveField(id)?.FieldType;
            var modType = Module.FindType(id.ExpressionString, Classes[CurrentMethod.Owner.FullName].Includes);
            if (modType is not null)
                return modType;
            this.LogError($"The name '{id}' does not exist in the current context.", id);
            return null;
        }
        public ManaField ResolveField(ManaClass targetType, IdentifierExpression target, IdentifierExpression id)
        {
            var field = targetType.FindField(id.ExpressionString);

            if (field is not null)
                return field;
            this.LogError($"'{targetType.FullName.NameWithNS}' does not contain " +
                          $"a definition for '{target.ExpressionString}' and " +
                          $"no extension method '{id.ExpressionString}' accepting " +
                          $"a first argument of type '{targetType.FullName.NameWithNS}' could be found.", id);
            return null;
        }
        public ManaMethod ResolveMethod(
            ManaClass targetType, 
            IdentifierExpression target, 
            IdentifierExpression id, 
            MethodInvocationExpression invocation)
        {
            var method = targetType.FindMethod(id.ExpressionString, invocation.Arguments.DetermineTypes(this));
            if (method is not null)
                return method;
            if (target is not null)
                this.LogError($"'{targetType.FullName.NameWithNS}' does not contain " +
                          $"a definition for '{target.ExpressionString}' and " +
                          $"no extension method '{id.ExpressionString}' accepting " +
                          $"a first argument of type '{targetType.FullName.NameWithNS}' could be found.", id);
            else
                this.LogError($"The name '{id}' does not exist in the current context.", id);
            return null;
        }
    }

    public class ManaScope
    {
        public ManaScope TopScope { get; }
        public List<ManaScope> Scopes { get; } = new ();
        public GeneratorContext Context { get; }

        public Dictionary<IdentifierExpression, ManaClass> variables { get; } = new();


        public ManaScope(GeneratorContext gen, ManaScope owner = null)
        {
            this.Context = gen;
            if (owner is null) 
                return;
            this.TopScope = owner;
            owner.Scopes.Add(this);
        }

        public ManaScope EnterScope() => new (Context, this);

        public bool HasVariable(IdentifierExpression id) 
            => variables.ContainsKey(id);

        public ManaScope DefineVariable(IdentifierExpression id, ManaClass type)
        {
            if (HasVariable(id))
            {
                Context.LogError($"A local variable named '{id}' is already defined in this scope", id);
                return this;
            }
            variables.Add(id, type);
            return this;
        }
    }

    public static class GeneratorExtension
    {
        public static bool ContainsField(this ManaClass @class, IdentifierExpression id) 
            => @class.Fields.Any(x => x.Name.Equals(id.ExpressionString));

        public static ManaField ResolveField(this ManaClass @class, IdentifierExpression id) 
            => @class.Fields.FirstOrDefault(x => x.Name.Equals(id.ExpressionString));

        public static void EmitUnary(this ILGenerator gen, UnaryExpressionSyntax node)
        {
            if (node.OperatorType == ExpressionType.NegateChecked && node.Operand.GetTypeCode().HasInteger())
            {
                var type = node.Operand.GetTypeCode();
                gen.EmitDefault(type);
                gen.EmitExpression(node.Operand);
                gen.EmitBinaryOperator(ExpressionType.SubtractChecked, type, type, type);
            }
            else
            {
                gen.EmitExpression(node.Operand);
                //gen.EmitUnaryOperator(node.NodeType, node.Operand.Type, node.Type);
            }
        }
        
        
        public static void EmitBinaryOperator(this ILGenerator gen, ExpressionType op, ManaTypeCode leftType, ManaTypeCode rightType, ManaTypeCode resultType)
        {
            switch (op)
            {
                case ExpressionType.Add:
                    gen.Emit(OpCodes.ADD);
                    break;
                case ExpressionType.Subtract:
                    gen.Emit(OpCodes.SUB);
                    break;
                case ExpressionType.Multiply:
                    gen.Emit(OpCodes.MUL);
                    break;
                case ExpressionType.Divide:
                    gen.Emit(OpCodes.DIV);
                    break;
                case ExpressionType.And:
                case ExpressionType.AndAlso:
                    gen.Emit(OpCodes.AND);
                    return;
                case ExpressionType.Or:
                case ExpressionType.OrElse:
                    gen.Emit(OpCodes.OR);
                    return;
                case ExpressionType.NotEqual:
                    if (leftType == ManaTypeCode.TYPE_BOOLEAN)
                        goto case ExpressionType.ExclusiveOr;
                    gen.Emit(OpCodes.EQL);
                    gen.Emit(OpCodes.LDC_I4_0);
                    goto case ExpressionType.Equal;
                case ExpressionType.Equal:
                    gen.Emit(OpCodes.EQL);
                    return;
                case ExpressionType.ExclusiveOr:
                    gen.Emit(OpCodes.XOR);
                    return;
                default:
                    throw new NotSupportedException();
            }
        }

        public static void EmitDefault(this ILGenerator gen, ManaTypeCode code)
        {
            switch (code)
            {
                case ManaTypeCode.TYPE_OBJECT:
                case ManaTypeCode.TYPE_STRING:
                    gen.Emit(OpCodes.LDNULL);
                    break;
                case ManaTypeCode.TYPE_CHAR:
                case ManaTypeCode.TYPE_I2:
                case ManaTypeCode.TYPE_BOOLEAN:
                    gen.Emit(OpCodes.LDC_I2_0);
                    break;
                case ManaTypeCode.TYPE_I4:
                    gen.Emit(OpCodes.LDC_I4_0);
                    break;
                case ManaTypeCode.TYPE_I8:
                    gen.Emit(OpCodes.LDC_I8_0);
                    break;
                case ManaTypeCode.TYPE_R4:
                    gen.Emit(OpCodes.LDC_F4, .0f);
                    break;
                case ManaTypeCode.TYPE_R8:
                    gen.Emit(OpCodes.LDC_F8, .0d);
                    break;
                default:
                    throw new NotSupportedException($"{code}");
            }
        }
        
        public static ManaTypeCode GetTypeCodeFromNumber(NumericLiteralExpressionSyntax number) =>
        number switch
        {
            ByteLiteralExpressionSyntax => ManaTypeCode.TYPE_U1,
            SByteLiteralExpressionSyntax => ManaTypeCode.TYPE_I1,
            Int16LiteralExpressionSyntax => ManaTypeCode.TYPE_I2,
            UInt16LiteralExpressionSyntax => ManaTypeCode.TYPE_U2,
            Int32LiteralExpressionSyntax => ManaTypeCode.TYPE_I4,
            UInt32LiteralExpressionSyntax => ManaTypeCode.TYPE_U4,
            Int64LiteralExpressionSyntax => ManaTypeCode.TYPE_I8,
            UInt64LiteralExpressionSyntax => ManaTypeCode.TYPE_U8,
            HalfLiteralExpressionSyntax => ManaTypeCode.TYPE_R2,
            SingleLiteralExpressionSyntax => ManaTypeCode.TYPE_R4,
            DoubleLiteralExpressionSyntax => ManaTypeCode.TYPE_R8,
            DecimalLiteralExpressionSyntax => ManaTypeCode.TYPE_R16,
            _ => throw new NotSupportedException($"{number} is not support number.")
        };
        
        public static void EmitExpression(this ILGenerator gen, ExpressionSyntax expr)
        {
            if (expr is LiteralExpressionSyntax literal)
            {
                gen.EmitLiteral(literal);
                return;
            }

            if (expr is NewExpressionSyntax @new)
            {
                gen.EmitCreateObject(@new);
                return;
            }

            if (expr is ArgumentExpression arg)
            {
                gen.EmitExpression(arg.Value);
                return;
            }

            throw new NotImplementedException();
        }

        public static bool CanImplicitlyCast(this ManaTypeCode code, NumericLiteralExpressionSyntax numeric)
        {
            if (code.IsCompatibleNumber(numeric.GetTypeCode()))
                return true;

            switch (code)
            {
                case ManaTypeCode.TYPE_I1:
                    return long.Parse(numeric.ExpressionString) is <= sbyte.MaxValue and >= sbyte.MinValue;
                case ManaTypeCode.TYPE_I2:
                    return long.Parse(numeric.ExpressionString) is <= short.MaxValue and >= short.MinValue;
                case ManaTypeCode.TYPE_I4:
                    return long.Parse(numeric.ExpressionString) is <= int.MaxValue and >= int.MinValue;

                case ManaTypeCode.TYPE_U1:
                    return ulong.Parse(numeric.ExpressionString) is <= byte.MaxValue and >= byte.MinValue;
                case ManaTypeCode.TYPE_U2:
                    return ulong.Parse(numeric.ExpressionString) is <= ushort.MaxValue and >= ushort.MinValue;
                case ManaTypeCode.TYPE_U4:
                    return ulong.Parse(numeric.ExpressionString) is <= uint.MaxValue and >= uint.MinValue;
            }

            return false;
        }

        public static ManaTypeCode GetTypeCode(this ExpressionSyntax exp)
        {
            if (exp is NumericLiteralExpressionSyntax num)
                return GetTypeCodeFromNumber(num);
            if (exp is BoolLiteralExpressionSyntax)
                return ManaTypeCode.TYPE_BOOLEAN;
            if (exp is StringLiteralExpressionSyntax)
                return ManaTypeCode.TYPE_STRING;
            if (exp is NullLiteralExpressionSyntax)
                return ManaTypeCode.TYPE_NONE;
            return ManaTypeCode.TYPE_CLASS;
        }
        public static void EmitCreateObject(this ILGenerator gen, NewExpressionSyntax @new)
        {
            var context = gen.ConsumeFromMetadata<GeneratorContext>("context");
            var type = context.ResolveType(@new.TargetType.Typeword);

            if (type.IsStatic)
            {
                context.LogError($"Cannot create an instance of the static class '{type}'", @new.TargetType);
                return;
            }
            
            foreach (var arg in @new.CtorArgs) 
                gen.EmitExpression(arg);
            gen.Emit(OpCodes.NEWOBJ, type);
            var ctor = type.FindMethod("ctor", @new.CtorArgs.DetermineTypes(context));
            
            if (ctor is null)
            {
                context.LogError(
                    $"'{type}' does not contain a constructor that takes '{@new.CtorArgs.Count}' arguments.",
                    @new.TargetType);
                return;
            }

            gen.Emit(OpCodes.CALL, ctor);
        }

        public static IEnumerable<ManaClass> DetermineTypes(this IEnumerable<ExpressionSyntax> exps, GeneratorContext context) 
            => exps.Select(x => x.DetermineType(context)).Where(x => x is not null /* if failed determine skip analyze */);

        public static ManaClass DetermineType(this ExpressionSyntax exp, GeneratorContext context)
        {
            if (exp.CanOptimizationApply())
                return exp.ForceOptimization().DetermineType(context);
            if (exp is LiteralExpressionSyntax literal)
                return literal.GetTypeCode().AsClass();
            if (exp is BinaryExpressionSyntax bin)
            {
                if (bin.OperatorType.IsLogic())
                    return ManaTypeCode.TYPE_BOOLEAN.AsClass();
                var (lt, rt) = bin.Fusce(context);

                return lt == rt ? lt : ExplicitConversion(lt, rt);
            }
            if (exp is NewExpressionSyntax @new)
                return context.ResolveType(@new.TargetType.Typeword);
            if (exp is MemberAccessExpression member)
                return member.ResolveType(context);
            context.LogError($"Cannot determine expression.", exp);
            return null;
        }

        public static ManaClass ResolveType(this MemberAccessExpression member, GeneratorContext context)
        {
            var chain = member.GetChain().ToArray();
            var lastToken = chain.Last();

            if (lastToken is MethodInvocationExpression method)
                return method.ResolveReturnType(context, chain);
            if (lastToken is IndexerExpression)
            {
                context.LogError($"indexer is not support.", lastToken);
                return null;
            }
            if (lastToken is OperatorExpressionSyntax)
                return chain.SkipLast(1).ResolveMemberType(context);
            if (lastToken is IdentifierExpression)
                return chain.ResolveMemberType(context);
            context.LogError($"Cannot determine expression.", lastToken);
            return null;
        }

        public static (ManaClass, ManaClass) Fusce(this BinaryExpressionSyntax binary, GeneratorContext context)
        {
            var lt = binary.Left.DetermineType(context);
            var rt = binary.Right.DetermineType(context);

            return (lt, rt);
        }

        public static ManaClass ResolveMemberType(this IEnumerable<ExpressionSyntax> chain, GeneratorContext context)
        {
            var t = default(ManaClass);
            var prev_id = default(IdentifierExpression);
            using var enumerator = chain.GetEnumerator();

            while (enumerator.MoveNext())
            {
                var exp = enumerator.Current;
                if (exp is not IdentifierExpression id)
                {
                    context.LogError($"Incorrect state of expression.", exp);
                    return null;
                }

                t = t is null ? 
                    context.ResolveScopedIdentifierType(id) : 
                    context.ResolveField(t, prev_id, id)?.FieldType;
                prev_id = id;
            }

            return t;
        }

        public static ManaClass ResolveReturnType(this MethodInvocationExpression member,
            GeneratorContext context, IEnumerable<ExpressionSyntax> chain)
        {
            var t = default(ManaClass);
            var prev_id = default(IdentifierExpression);
            var enumerator = chain.ToArray();
            for (var i = 0; i != enumerator.Length; i++)
            {
                var exp = enumerator[i] as IdentifierExpression;

                if (i + 1 == enumerator.Length-1)
                    return context.ResolveMethod(t ?? context.CurrentMethod.Owner, prev_id, exp, member)
                        ?.ReturnType;
                t = t is null ? 
                    context.ResolveScopedIdentifierType(exp) : 
                    context.ResolveField(t, prev_id, exp)?.FieldType;
                prev_id = exp;
            }

            context.LogError($"Incorrect state of expression.", member);
            return null;
        }


        public static ManaClass ExplicitConversion(ManaClass t1, ManaClass t2)
        {
            throw new NotImplementedException();
        }

        public static void EmitThrow(this ILGenerator generator, QualityTypeName type)
        {
            generator.Emit(OpCodes.NEWOBJ, type);
            generator.Emit(OpCodes.CALL, GetDefaultCtor(type));
            generator.Emit(OpCodes.THROW);
        }
        public static ManaMethod GetDefaultCtor(QualityTypeName t) => throw new NotImplementedException();
        public static void EmitIfElse(this ILGenerator generator, IfStatementSyntax ifStatement)
        {
            var elseLabel = generator.DefineLabel();
            var ctx = generator.ConsumeFromMetadata<GeneratorContext>("context");
            var expType = ifStatement.Expression.DetermineType(ctx);

            if (ifStatement.Expression is BoolLiteralExpressionSyntax @bool)
            {
                if (@bool.Value)
                    generator.EmitStatement(ifStatement.ThenStatement);
                else
                    generator.Emit(OpCodes.JMP, elseLabel);
            }
            else if (expType.TypeCode == ManaTypeCode.TYPE_BOOLEAN)
            {
                generator.EmitExpression(ifStatement.Expression);
                generator.Emit(OpCodes.JMP_F, elseLabel);
                generator.EmitStatement(ifStatement.ThenStatement);
            }
            else
            {
                ctx.LogError($"Cannot implicitly convert type '{expType}' to 'Boolean'", ifStatement.Expression);
                return;
            }
            generator.UseLabel(elseLabel);

            if (ifStatement.ElseStatement is null)
                return;

            generator.EmitStatement(ifStatement.ElseStatement);
        }

        public static void EmitStatement(this ILGenerator generator, StatementSyntax statement)
        {
            if (statement is ReturnStatementSyntax ret1)
                generator.EmitReturn(ret1);
            else if (statement is IfStatementSyntax theIf)
                generator.EmitIfElse(theIf);
            else if (statement is WhileStatementSyntax @while)
                generator.EmitWhileStatement(@while);
            else if (statement is QualifiedExpressionStatement {Value: MemberAccessExpression} qes)
                generator.EmitCall((MemberAccessExpression) qes.Value);
            else
                throw new NotImplementedException();
        }

        public static void EmitCall(this ILGenerator generator, MemberAccessExpression qes)
        {
            var ctx = generator.ConsumeFromMetadata<GeneratorContext>("context");

            Assert.IsType<IdentifierExpression>(qes.Start);
            var chain = qes.Chain.ToArray();
            var args = (MethodInvocationExpression) chain[0];
            var method = ctx.ResolveMethod(ctx.CurrentMethod.Owner, null, qes.Start as IdentifierExpression,
                (MethodInvocationExpression)chain[0]);

            foreach (var arg in args.Arguments)
            {
                generator.EmitExpression(arg);
            }
            
            generator.Emit(OpCodes.CALL, method);
        }
        public static void EmitNumericLiteral(this ILGenerator generator, NumericLiteralExpressionSyntax literal)
        {
            switch (literal)
            {
                case Int16LiteralExpressionSyntax i16:
                {
                    if (i16 is {Value: 0}) generator.Emit(OpCodes.LDC_I2_0);
                    if (i16 is {Value: 1}) generator.Emit(OpCodes.LDC_I2_1);
                    if (i16 is {Value: 2}) generator.Emit(OpCodes.LDC_I2_2);
                    if (i16 is {Value: 3}) generator.Emit(OpCodes.LDC_I2_3);
                    if (i16 is {Value: 4}) generator.Emit(OpCodes.LDC_I2_4);
                    if (i16 is {Value: 5}) generator.Emit(OpCodes.LDC_I2_5);
                    if (i16.Value > 5 || i16.Value < 0)
                        generator.Emit(OpCodes.LDC_I2_S, i16.Value);
                    break;
                }
                case Int32LiteralExpressionSyntax i32:
                {
                    if (i32 is {Value: 0}) generator.Emit(OpCodes.LDC_I4_0);
                    if (i32 is {Value: 1}) generator.Emit(OpCodes.LDC_I4_1);
                    if (i32 is {Value: 2}) generator.Emit(OpCodes.LDC_I4_2);
                    if (i32 is {Value: 3}) generator.Emit(OpCodes.LDC_I4_3);
                    if (i32 is {Value: 4}) generator.Emit(OpCodes.LDC_I4_4);
                    if (i32 is {Value: 5}) generator.Emit(OpCodes.LDC_I4_5);
                    if (i32.Value > 5 || i32.Value < 0) 
                        generator.Emit(OpCodes.LDC_I4_S, i32.Value);
                    break;
                }
                case Int64LiteralExpressionSyntax i64:
                {
                    if (i64 is {Value: 0}) generator.Emit(OpCodes.LDC_I8_0);
                    if (i64 is {Value: 1}) generator.Emit(OpCodes.LDC_I8_1);
                    if (i64 is {Value: 2}) generator.Emit(OpCodes.LDC_I8_2);
                    if (i64 is {Value: 3}) generator.Emit(OpCodes.LDC_I8_3);
                    if (i64 is {Value: 4}) generator.Emit(OpCodes.LDC_I8_4);
                    if (i64 is {Value: 5}) generator.Emit(OpCodes.LDC_I8_5);
                    if (i64.Value > 5 || i64.Value < 0)
                        generator.Emit(OpCodes.LDC_I8_S, i64.Value);
                    break;
                }
                case DecimalLiteralExpressionSyntax f128:
                {
                    generator.Emit(OpCodes.LDC_F16, f128.Value);
                    break;
                }
                case DoubleLiteralExpressionSyntax f64:
                {
                    generator.Emit(OpCodes.LDC_F8, f64.Value);
                    break;
                }
                case SingleLiteralExpressionSyntax f32:
                {
                    generator.Emit(OpCodes.LDC_F4, f32.Value);
                    break;
                }
                case HalfLiteralExpressionSyntax h32:
                {
                    generator.Emit(OpCodes.LDC_F2, h32.Value);
                    break;
                }
                default:
                    throw new NotImplementedException();
            }
        }

        public static void EmitLiteral(this ILGenerator generator, LiteralExpressionSyntax literal)
        {
            if (literal is NumericLiteralExpressionSyntax numeric)
                generator.EmitNumericLiteral(numeric);
            else if (literal is StringLiteralExpressionSyntax stringLiteral)
                generator.Emit(OpCodes.LDC_STR, UnEscapeSymbols(stringLiteral.Value));
            else if (literal is BoolLiteralExpressionSyntax boolLiteral)
                generator.Emit(boolLiteral.Value ? OpCodes.LDC_I2_1 : OpCodes.LDC_I2_0);
            else if (literal is NullLiteralExpressionSyntax)
                generator.Emit(OpCodes.LDNULL);
        }

        private static string UnEscapeSymbols(string str)
        {
            return Regex.Unescape(str);
        }
        
        public static void EmitReturn(this ILGenerator generator, ReturnStatementSyntax statement)
        {
            if (statement is not { Expression: { } })
            {
                generator.Emit(OpCodes.RET);
                return;
            }
            if (statement.Expression is LiteralExpressionSyntax literal)
            {
                generator.EmitLiteral(literal);
                generator.Emit(OpCodes.RET);
                return;
            }

            if (statement.Expression.ExpressionString == "Infinity")
            {
                generator.Emit(OpCodes.LDC_F4, float.PositiveInfinity);
                generator.Emit(OpCodes.RET);
                return;
            }
            if (statement.Expression.ExpressionString == "(-Infinity)")
            {
                generator.Emit(OpCodes.LDC_F4, float.NegativeInfinity);
                generator.Emit(OpCodes.RET);
                return;
            }
            if (statement.Expression.ExpressionString == "NaN")
            {
                generator.Emit(OpCodes.LDC_F4, float.NaN);
                generator.Emit(OpCodes.RET);
                return;
            }

            var @class = generator._methodBuilder.classBuilder;
            var @method = generator._methodBuilder;

            if (statement.Expression is MemberAccessExpression unnamed02)
            {
                var type = generator
                    ._methodBuilder
                    .moduleBuilder
                    .FindType(unnamed02.Start.ExpressionString, @class.Includes);

                var methodName = unnamed02.Chain.First().ExpressionString;

                var call_method = type.FindMethod(methodName);

                generator.Emit(OpCodes.CALL, call_method);
                generator.Emit(OpCodes.RET);
                return;
            }

            if (statement.Expression.Kind == SyntaxType.Expression)
            {
                generator.Emit(OpCodes.LDF, new FieldName(statement.Expression.ExpressionString));
                generator.Emit(OpCodes.RET);
                return;
            }
        }

        public static void EmitWhileStatement(this ILGenerator gen, WhileStatementSyntax @while)
        {
            var ctx = gen.ConsumeFromMetadata<GeneratorContext>("context");
            var start = gen.DefineLabel();
            var end = gen.DefineLabel();
            var expType = @while.Expression.DetermineType(ctx);
            gen.UseLabel(start);
            if (expType.FullName == ManaTypeCode.TYPE_BOOLEAN.AsClass().FullName)
            {
                gen.EmitExpression(@while.Expression);
                gen.Emit(OpCodes.JMP_F, end);
            }
            else // todo implicit boolean
            {
                ctx.LogError($"Cannot implicitly convert type '{expType}' to 'Boolean'", @while.Expression);
                return;
            }
            gen.EmitStatement(@while.Statement);
            gen.Emit(OpCodes.JMP, start);
            gen.UseLabel(end);
        }
    }
}