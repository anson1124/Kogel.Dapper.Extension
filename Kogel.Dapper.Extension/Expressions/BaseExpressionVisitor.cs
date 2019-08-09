﻿using Dapper;
using Kogel.Dapper.Extension.Core.Interfaces;
using Kogel.Dapper.Extension.Exception;
using Kogel.Dapper.Extension.Extension;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace Kogel.Dapper.Extension.Expressions
{
    /// <summary>
    /// 实现表达式解析的基类
    /// </summary>
    public class BaseExpressionVisitor : ExpressionVisitor
    {
        /// <summary>
        /// 字段集合
        /// </summary>
        protected List<string> FieldList { get; set; }
        protected DynamicParameters Param { get; set; }
        private IProviderOption providerOption { get; set; }
        public BaseExpressionVisitor(IProviderOption providerOption)
        {
            this.FieldList = new List<string>();
            this.Param = new DynamicParameters();
            this.providerOption = providerOption;
        }
        /// <summary>
        /// 有+ - * /需要拼接的对象
        /// </summary>
        /// <param name="node"></param>
        /// <returns></returns>
        protected override Expression VisitBinary(BinaryExpression node)
        {
            var binary = new BinaryExpressionVisitor(node,providerOption);
            GenerateField(binary.SpliceField.ToString());
            this.Param.AddDynamicParams(binary.Param);
            return node;
        }
        /// <summary>
        /// 成员对象
        /// </summary>
        /// <param name="node"></param>
        /// <returns></returns>
        protected override Expression VisitConstant(ConstantExpression node)
        {
            GenerateField(GetFieldValue(node));
            return node;
        }
        /// <summary>
        /// 值对象
        /// </summary>
        /// <param name="node"></param>
        /// <returns></returns>
        protected override Expression VisitMember(MemberExpression node)
        {
            var member = EntityCache.QueryEntity(node.Member.DeclaringType);
            string fieldName = member.FieldPairs[node.Member.Name];
            string field = member.Name + "." + fieldName;
            GenerateField(field);
            return node;
        }

        /// <summary>
        /// 待执行的方法对象
        /// </summary>
        /// <param name="node"></param>
        /// <returns></returns>
        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.DeclaringType.FullName.Contains("Convert"))//使用convert函数里待执行的sql数据
            {
                Visit(node.Arguments[0]);
            }
            else if (node.Method.DeclaringType.FullName.Contains("Kogel.Dapper.Extension"))
            {
                DynamicParameters parameters = new DynamicParameters();
                GenerateField("(" + node.MethodCallExpressionToSql(ref parameters) + ")");
                Param.AddDynamicParams(parameters);
            }
            else
            {
                GenerateField(GetFieldValue(node));
            }
            return node;
        }
        /// <summary>
        /// 生成字段
        /// </summary>
        /// <param name="field"></param>
        protected virtual void GenerateField(string field)
        {
            FieldList.Add(field);
        }
        /// <summary>
        /// 获取字段的值
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        protected virtual string GetFieldValue(Expression expression)
        {
            object value;
            if (expression is ConstantExpression)
            {
                value = ((ConstantExpression)expression).Value;
            }
            else
            {
                value = expression.ToConvertAndGetValue();
            }
            return value.ToString();
        }
    }
    public class WhereExpressionVisitor : BaseExpressionVisitor
    {
        private string FieldName { get; set; }//字段
        private string ParamName { get => (providerOption.ParameterPrefix + FieldName.Replace(".", "_") + "_" + Param.ParameterNames.Count()); }//带参数标识的
        internal StringBuilder SpliceField { get; set; }
        internal new DynamicParameters Param { get; set; }
        internal IProviderOption providerOption { get; set; }
        public WhereExpressionVisitor(IProviderOption providerOption):base(providerOption)
        {
            this.providerOption = providerOption;
            this.SpliceField = new StringBuilder();
            this.Param = new DynamicParameters();
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            //使用convert函数里待执行的sql数据
            if (node.Method.DeclaringType.FullName.Contains("Convert"))
            {
                Visit(node.Arguments[0]);
            }
            else if (node.Method.DeclaringType.FullName.Equals("Kogel.Dapper.Extension.ExpressExpansion"))//自定义扩展方法
            {
                Visit(node.Arguments[0]);
                Operation(node);
            }
            else if (node.Method.DeclaringType.FullName.Contains("Kogel.Dapper.Extension"))
            {
                base.VisitMethodCall(node);
            }
            else
            {
                Operation(node);
            }
            return node;
        }
        /// <summary>
        /// 重写成员对象，得到字段名称
        /// </summary>
        /// <param name="node"></param>
        /// <returns></returns>
        protected override Expression VisitMember(MemberExpression node)
        {
            var member = EntityCache.QueryEntity(node.Member.DeclaringType);
            string fieldName = member.FieldPairs[node.Member.Name];
            this.FieldName = member.Name + "." + fieldName;
            SpliceField.Append(this.FieldName);

            //时间类型有时会进入此处
            if (node.Type == typeof(DateTime))
            {
                SpliceField = SpliceField.Replace("DateTime.Now", providerOption.GetDate());
            }
            return node;
        }
        /// <summary>
        /// 重写值对象，记录参数
        /// </summary>
        /// <param name="node"></param>
        /// <returns></returns>
        protected override Expression VisitConstant(ConstantExpression node)
        {
            SpliceField.Append(ParamName);
            Param.Add(ParamName, node.ToConvertAndGetValue());
            return node;
        }
        private void Operation(MethodCallExpression node)
        {
            switch (node.Method.Name)
            {
                case "Contains":
                    {
                        Visit(node.Object);
                        this.SpliceField.Append($" LIKE {ParamName}");
                        var argumentExpression = (ConstantExpression)node.Arguments[0];
                        Param.Add(ParamName, "%" + argumentExpression.ToConvertAndGetValue() + "%");
                    }
                    break;
                case "Equals":
                    {
                        Visit(node.Object);
                        this.SpliceField.Append($" = {ParamName}");
                        var argumentExpression = (ConstantExpression)node.Arguments[0];
                        Param.Add(ParamName, argumentExpression.ToConvertAndGetValue());
                    }
                    break;
                case "In":
                    {
                        this.SpliceField.Append($" IN {ParamName}");
                        object value = node.Arguments[1].ToConvertAndGetValue();
                        Param.Add(ParamName, value);
                    }
                    break;
                case "NotIn":
                    {
                        this.SpliceField.Append($" NOT IN {ParamName}");
                        object value = node.Arguments[1].ToConvertAndGetValue();
                        Param.Add(ParamName, value);
                    }
                    break;
                case "IsNull":
                    {
                        this.SpliceField.Append(" IS NULL");
                    }
                    break;
                case "IsNotNull":
                    {
                        this.SpliceField.Append(" IS NOT NULL");
                    }
                    break;
                case "Between":
                    {
                        string fromParam = ParamName + "_From";
                        string toParam = ParamName + "_To";
                        this.SpliceField.Append($" BETWEEN {fromParam} AND {toParam}");
                        Param.Add(fromParam, node.Arguments[1].ToConvertAndGetValue());
                        Param.Add(toParam, node.Arguments[2].ToConvertAndGetValue());
                    }
                    break;
                #region Convert转换计算
                case "ToInt32":
                case "ToString":
                case "ToDecimal":
                case "ToDouble":
                    {
                        Visit(node.Object);
                        if (node.Arguments.Count > 0)//Convert.ToInt32("xxx") or Convert.ToString("xxx")
                        {
                            this.SpliceField.AppendFormat(" {0}", ParamName);
                            var argumentExpression = (ConstantExpression)node.Arguments[0];
                            Param.Add(ParamName, argumentExpression.ToConvertAndGetValue());
                        }
                        else
                        {
                            //xxx.ToString
                        }
                    }
                    break;
                #endregion
                #region 时间计算
                case "AddYears":
                case "AddMonths":
                case "AddDays":
                case "AddHours":
                case "AddMinutes":
                case "AddSeconds":
                    {
                        if (!(node.Object is MemberExpression))//数值比对（DateTime.Now.AddDays）
                        {
                            object time = node.ToConvertAndGetValue();
                            this.SpliceField.AppendFormat(" {0}", ParamName);
                            Param.Add(ParamName, time);
                        }
                        else//字段比对(类似CreateDate.AddDays)
                        {
                            var member = (MemberExpression)(node.Object);
                            var entity = EntityCache.QueryEntity(member.Member.DeclaringType);
                            var dateOption = (DateOption)Enum.Parse(typeof(DateOption), node.Method.Name);
                            string fieldName = entity.FieldPairs[member.Member.Name];
                            string sqlCombine = providerOption.CombineDate(dateOption, entity.Name, fieldName, node.Arguments[0].ToConvertAndGetValue().ToString());
                            this.SpliceField.Append(sqlCombine);
                            //重新计算参数名称
                            FieldName = entity.Name + "_" + fieldName;
                        }
                    }
                    break;
                #endregion
                default:
                    throw new DapperExtensionException("the expression is no support this function");
            }
        }
    }
    /// <summary>
    /// 转用于解析二元表达式
    /// </summary>
    public class BinaryExpressionVisitor : WhereExpressionVisitor
    {
        public BinaryExpressionVisitor(BinaryExpression expression, IProviderOption providerOption):base(providerOption)
        {
            
            SpliceField = new StringBuilder();
            Param = new DynamicParameters();
            SpliceField.Append("(");
            Visit(expression);
            SpliceField.Append(")");
        }

        protected override Expression VisitBinary(BinaryExpression node)
        {
            Visit(node.Left);
            SpliceField.Append(node.GetExpressionType());
            Visit(node.Right);
            return node;
        }
    }
}
