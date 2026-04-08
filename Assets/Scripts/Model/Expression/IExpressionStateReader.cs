using ChillAI.Core;

namespace ChillAI.Model.Expression
{
    public interface IExpressionStateReader
    {
        ExpressionType CurrentExpression { get; }
    }
}
