﻿using System;
using System.ComponentModel;
using System.Reflection;
using System.Windows;

namespace Stylet.Xaml
{
    /// <summary>
    /// Created by ActionExtension, this can return a delegate suitable adding binding to an event, and can call a method on the View.ActionTarget
    /// </summary>
    public class EventAction
    {
        private static readonly ILogger logger = LogManager.GetLogger(typeof(EventAction));

        /// <summary>
        /// View whose View.ActionTarget we watch
        /// </summary>
        private DependencyObject subject;

        /// <summary>
        /// Property on the WPF element we're returning a delegate for
        /// </summary>
        private EventInfo targetProperty;

        /// <summary>
        /// The MyMethod in {s:Action MyMethod}, this is what we call when the event's fired
        /// </summary>
        private string methodName;

        /// <summary>
        /// MethodInfo for the method to call. This has to exist, or we throw a wobbly
        /// </summary>
        private MethodInfo targetMethodInfo;

        private object target;

        private ActionUnavailableBehaviour targetNullBehaviour;
        private ActionUnavailableBehaviour actionNonExistentBehaviour;

        /// <summary>
        /// Create a new EventAction
        /// </summary>
        /// <param name="subject">View whose View.ActionTarget we watch</param>
        /// <param name="targetProperty">Property on the WPF element we're returning a delegate for</param>
        /// <param name="methodName">The MyMethod in {s:Action MyMethod}, this is what we call when the event's fired</param>
        /// <param name="targetNullBehaviour">Behaviour for it the relevant View.ActionTarget is null</param>
        /// <param name="actionNonExistentBehaviour">Behaviour for if the action doesn't exist on the View.ActionTarget</param>
        public EventAction(DependencyObject subject, EventInfo targetProperty, string methodName, ActionUnavailableBehaviour targetNullBehaviour, ActionUnavailableBehaviour actionNonExistentBehaviour)
        {
            if (targetNullBehaviour == ActionUnavailableBehaviour.Disable)
                throw new ArgumentException("Setting NullTarget = Disable is unsupported when used on an Event");
            if (actionNonExistentBehaviour == ActionUnavailableBehaviour.Disable)
                throw new ArgumentException("Setting ActionNotFound = Disable is unsupported when used on an Event");

            this.subject = subject;
            this.targetProperty = targetProperty;
            this.methodName = methodName;
            this.targetNullBehaviour = targetNullBehaviour;
            this.actionNonExistentBehaviour = actionNonExistentBehaviour;

            this.UpdateMethod();

            // Observe the View.ActionTarget for changes, and re-bind the guard property and MethodInfo if it changes
            DependencyPropertyDescriptor.FromProperty(View.ActionTargetProperty, typeof(View)).AddValueChanged(this.subject, (o, e) => this.UpdateMethod());
        }

        private void UpdateMethod()
        {
            var newTarget = View.GetActionTarget(this.subject);
            MethodInfo targetMethodInfo = null;

            // If it's being set to the initial value, ignore it
            // At this point, we're executing the View's InitializeComponent method, and the ActionTarget hasn't yet been assigned
            // If they've opted to throw if the target is null, then this will cause that exception.
            // We'll just wait until the ActionTarget is assigned, and we're called again
            if (newTarget == View.InitialActionTarget)
                return;

            if (newTarget == null)
            {
                if (this.targetNullBehaviour == ActionUnavailableBehaviour.Throw)
                {
                    var e = new ArgumentException(String.Format("ActionTarget on element {0} is null (method name is {1})", this.subject, this.methodName));
                    logger.Error(e);
                    throw e;
                }
                else
                {
                    logger.Warn("ActionTarget on element {0} is null (method name is {1}), nut NullTarget is not Throw, so carrying on", this.subject, this.methodName);
                }
            }
            else
            {
                var newTargetType = newTarget.GetType();
                targetMethodInfo = newTargetType.GetMethod(this.methodName);
                if (targetMethodInfo == null)
                {
                    if (this.actionNonExistentBehaviour == ActionUnavailableBehaviour.Throw)
                    {
                        var e = new ArgumentException(String.Format("Unable to find method {0} on {1}", this.methodName, newTargetType.Name));
                        logger.Error(e);
                        throw e;
                    }
                    else
                    {
                        logger.Warn("Unable to find method {0} on {1}, but ActionNotFound is not Throw, so carrying on", this.methodName, newTargetType.Name);
                    }
                }
                else
                {
                    var methodParameters = targetMethodInfo.GetParameters();
                    if (methodParameters.Length > 1 || (methodParameters.Length == 1 && !methodParameters[0].ParameterType.IsAssignableFrom(typeof(RoutedEventArgs))))
                    {
                        var e = new ArgumentException(String.Format("Method {0} on {1} must have zero parameters, or a single parameter accepting a RoutedEventArgs", this.methodName, newTargetType.Name));
                        logger.Error(e);
                        throw e;
                    }
                }
            }

            this.target = newTarget;
            this.targetMethodInfo = targetMethodInfo;
        }

        /// <summary>
        /// Return a delegate which can be added to the targetProperty
        /// </summary>
        public RoutedEventHandler GetDelegate()
        {
            return new RoutedEventHandler(this.InvokeCommand);
        }

        private void InvokeCommand(object sender, RoutedEventArgs e)
        {
            // Any throwing will have been handled above
            if (this.target == null || this.targetMethodInfo == null)
                return;

            var parameters = this.targetMethodInfo.GetParameters().Length == 1 ? new object[] { e } : null;

            logger.Info("Invoking method {0} on target {1} with parameters ({2})", this.methodName, this.target, parameters == null ? "none" : String.Join(", ", parameters));

            this.targetMethodInfo.Invoke(this.target, parameters);
        }
    }
}
