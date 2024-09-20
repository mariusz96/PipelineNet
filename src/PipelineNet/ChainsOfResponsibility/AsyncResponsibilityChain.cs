﻿using PipelineNet.Middleware;
using PipelineNet.MiddlewareResolver;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace PipelineNet.ChainsOfResponsibility
{
    /// <summary>
    /// Defines the asynchronous chain of responsibility.
    /// </summary>
    /// <typeparam name="TParameter">The input type for the chain.</typeparam>
    /// <typeparam name="TReturn">The return type of the chain.</typeparam>
    public class AsyncResponsibilityChain<TParameter, TReturn> : AsyncBaseMiddlewareFlow<IAsyncMiddleware<TParameter, TReturn>, ICancellableAsyncMiddleware<TParameter, TReturn>>,
        IAsyncResponsibilityChain<TParameter, TReturn>
    {
        private Func<TParameter, Task<TReturn>> _finallyFunc;

        /// <summary>
        /// Creates a new asynchronous chain of responsibility.
        /// </summary>
        /// <param name="middlewareResolver">The resolver used to create the middleware types.</param>
        public AsyncResponsibilityChain(IMiddlewareResolver middlewareResolver) : base(middlewareResolver)
        {
        }

        /// <summary>
        /// Chains a new middleware to the chain of responsibility.
        /// Middleware will be executed in the same order they are added.
        /// </summary>
        /// <typeparam name="TMiddleware">The new middleware being added.</typeparam>
        /// <returns>The current instance of <see cref="IAsyncResponsibilityChain{TParameter, TReturn}"/>.</returns>
        public IAsyncResponsibilityChain<TParameter, TReturn> Chain<TMiddleware>() where TMiddleware : IAsyncMiddleware<TParameter, TReturn>
        {
            MiddlewareTypes.Add(typeof(TMiddleware));
            return this;
        }

        /// <summary>
        /// Chains a new cancellable middleware to the chain of responsibility.
        /// Middleware will be executed in the same order they are added.
        /// </summary>
        /// <typeparam name="TCancellableMiddleware">The new middleware being added.</typeparam>
        /// <returns>The current instance of <see cref="IAsyncResponsibilityChain{TParameter, TReturn}"/>.</returns>
        public IAsyncResponsibilityChain<TParameter, TReturn> ChainCancellable<TCancellableMiddleware>() where TCancellableMiddleware : ICancellableAsyncMiddleware<TParameter, TReturn>
        {
            MiddlewareTypes.Add(typeof(TCancellableMiddleware));
            return this;
        }

        /// <summary>
        /// Chains a new middleware type to the chain of responsibility.
        /// Middleware will be executed in the same order they are added.
        /// </summary>
        /// <param name="middlewareType">The middleware type to be executed.</param>
        /// <exception cref="ArgumentException">Thrown if the <paramref name="middlewareType"/> is 
        /// not an implementation of <see cref="IAsyncMiddleware{TParameter, TReturn}"/> or <see cref="ICancellableAsyncMiddleware{TParameter, TReturn}"/>.</exception>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="middlewareType"/> is null.</exception>
        /// <returns>The current instance of <see cref="IAsyncResponsibilityChain{TParameter, TReturn}"/>.</returns>
        public IAsyncResponsibilityChain<TParameter, TReturn> Chain(Type middlewareType)
        {
            base.AddMiddleware(middlewareType);
            return this;
        }

        /// <summary>
        /// Executes the configured chain of responsibility.
        /// </summary>
        /// <param name="parameter"></param>
        public async Task<TReturn> Execute(TParameter parameter) =>
            await Execute(parameter, default).ConfigureAwait(false);

        /// <summary>
        /// Executes the configured chain of responsibility.
        /// </summary>
        /// <param name="parameter"></param>
        /// <param name="cancellationToken">The cancellation token that will be passed to all middleware.</param>
        public async Task<TReturn> Execute(TParameter parameter, CancellationToken cancellationToken)
        {
            if (MiddlewareTypes.Count == 0)
                return default(TReturn);

            int index = 0;
            Func<TParameter, Task<TReturn>> func = null;
            func = async (param) =>
            {
                MiddlewareResolverResult resolverResult = null;
                try
                {
                    var type = MiddlewareTypes[index];
                    resolverResult = MiddlewareResolver.Resolve(type);

                    index++;
                    // If the current instance of middleware is the last one in the list,
                    // the "next" function is assigned to the finally function or a 
                    // default empty function.
                    if (index == MiddlewareTypes.Count)
                        func = this._finallyFunc ?? ((p) => Task.FromResult(default(TReturn)));

                    if (resolverResult == null || resolverResult.Middleware == null)
                    {
                        throw new InvalidOperationException($"'{MiddlewareResolver.GetType()}' failed to resolve middleware of type '{type}'.");
                    }

                    if (resolverResult.IsDisposable && !(resolverResult.Middleware is IDisposable
#if NETSTANDARD2_1_OR_GREATER
                        || resolverResult.Middleware is IAsyncDisposable
#endif
    ))
                    {
                        throw new InvalidOperationException($"'{resolverResult.Middleware.GetType()}' type does not implement IDisposable" +
#if NETSTANDARD2_1_OR_GREATER
                            " or IAsyncDisposable" +
#endif
                            ".");
                    }

                    if (resolverResult.Middleware is ICancellableAsyncMiddleware<TParameter, TReturn> cancellableMiddleware)
                    {
                        return await cancellableMiddleware.Run(param, func, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        var middleware = (IAsyncMiddleware<TParameter, TReturn>)resolverResult.Middleware;
                        return await middleware.Run(param, func).ConfigureAwait(false);
                    }
                }
                finally
                {
                    if (resolverResult != null && resolverResult.IsDisposable)
                    {
                        var middleware = resolverResult.Middleware;
                        if (middleware != null)
                        {
#if NETSTANDARD2_1_OR_GREATER
                            if (middleware is IAsyncDisposable asyncDisposable)
                            {
                                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                            }
                            else
#endif
                            if (middleware is IDisposable disposable)
                            {
                                disposable.Dispose();
                            }
                        }
                    }
                }
            };

            return await func(parameter).ConfigureAwait(false);
        }

        /// <summary>
        /// Sets the function to be executed at the end of the chain as a fallback.
        /// A chain can only have one finally function. Calling this method more
        /// a second time will just replace the existing finally <see cref="Func{TParameter, TResult}"/>.
        /// </summary>
        /// <param name="finallyFunc">The function that will be execute at the end of chain.</param>
        /// <returns>The current instance of <see cref="IAsyncResponsibilityChain{TParameter, TReturn}"/>.</returns>
        public IAsyncResponsibilityChain<TParameter, TReturn> Finally(Func<TParameter, Task<TReturn>> finallyFunc)
        {
            this._finallyFunc = finallyFunc;
            return this;
        }
    }
}
