﻿//
//  Copyright (c) Microsoft Corporation. All rights reserved.
//  Licensed under the MIT License. See License.txt in the project root for license information.
//
// This file contain implementations details that are subject to change without notice.
// Use at your own risk.
//
namespace Microsoft.VisualStudio.Text.EditorOptions.Implementation
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Specialized;
    using System.Globalization;
    using System.Linq;
    using Microsoft.VisualStudio.Text.Editor;
    using Microsoft.VisualStudio.Text.Utilities;
    using Microsoft.VisualStudio.Utilities;

    internal class EditorOptions : IEditorOptions
    {
        IPropertyOwner Scope { get; set; }

        HybridDictionary OptionsSetLocally { get; set; }

        private EditorOptionsFactoryService _factory;

        FrugalList<WeakReference> DerivedEditorOptions = new FrugalList<WeakReference>();

        internal EditorOptions(EditorOptions parent,
                               IPropertyOwner scope,
                               EditorOptionsFactoryService factory)
        {
            _parent = parent;
            _factory = factory;
            this.Scope = scope;

            this.OptionsSetLocally = new HybridDictionary();

            if (parent != null)
            {
                parent.AddDerivedOptions(this);
            }
        }

        #region IEditorOptions Members

        private EditorOptions _parent;
        public IEditorOptions Parent 
        { 
            get
            {
                return _parent;
            }
            set
            {
                if (_parent == value)
                    return;

                // _parent == null => this is the global options instance
                if (_parent == null)
                    throw new InvalidOperationException("Cannot change the Parent of the global options.");

                if (value == null)
                    throw new ArgumentNullException(nameof(value));

                if (value == this)
                    throw new ArgumentException("The Parent of this instance of IEditorOptions cannot be set to itself.");

                EditorOptions newParent = value as EditorOptions;

                if (newParent == null)
                    throw new ArgumentException("New parent must be an instance of IEditorOptions generated by the same factory as this instance.");

                var oldParent = _parent;

                _parent.RemovedDerivedOptions(this);
                _parent = newParent;
                _parent.AddDerivedOptions(this);

                this.CheckForCycle();

                // TODO: Should we be more specific?  Should there be a
                // version of OptionsChanged that says "everything has changed"?
                
                // Send out an event for each supported option that isn't already
                // set locally (since the update in parent won't change the
                // observed value).
                foreach (var definition in _factory.GetInstantiatedOptions(this.Scope))
                {
                    if (!this.OptionsSetLocally.Contains(definition.Name))
                    {
                        object oldValue = oldParent.GetOptionForChild(definition);
                        object newValue = _parent.GetOptionForChild(definition);

                        if (!object.Equals(oldValue, newValue))
                            RaiseChangedEvent(definition);
                    }
                }
            }
        }

        public T GetOptionValue<T>(string optionId)
        {
            var definition = _factory.GetOptionDefinitionOrThrow(optionId);

            if (!typeof(T).IsAssignableFrom(definition.ValueType))
                throw new InvalidOperationException("Invalid type requested for the given option.");

            object value = this.GetOptionValue(definition);
            return (T)value;
        }

        public T GetOptionValue<T>(EditorOptionKey<T> key)
        {
            return GetOptionValue<T>(key.Name);
        }

        public object GetOptionValue(string optionId)
        {
            return GetOptionValue(_factory.GetOptionDefinitionOrThrow(optionId));
        }

        private object GetOptionValue(EditorOptionDefinition definition)
        {
            object value;

            if (!TryGetOption(definition, out value))
                throw new ArgumentException(string.Format(CultureInfo.CurrentCulture, "The specified option is not valid in this scope: {0}", definition.Name));

            return value;
        }

        public void SetOptionValue(string optionId, object value)
        {
            EditorOptionDefinition definition = _factory.GetOptionDefinitionOrThrow(optionId);

            // Make sure the type of the provided value is correct
            if (!definition.ValueType.IsAssignableFrom(value.GetType()))
            {
                throw new ArgumentException("Specified option value is of an invalid type", nameof(value));
            }
            // Make sure the option is valid, also
            else if(!definition.IsValid(ref value))
            {
                throw new ArgumentException("The supplied value failed validation for the option.", nameof(value));
            }
            // Finally, set the option value locally
            else
            {
                object currentValue = this.GetOptionValue(definition);
                OptionsSetLocally[optionId] = value;

                if (!object.Equals(currentValue, value))
                {
                    RaiseChangedEvent(definition);
                }
            }
        }

        public void SetOptionValue<T>(EditorOptionKey<T> key, T value)
        {
            SetOptionValue(key.Name, value);
        }

        public bool IsOptionDefined(string optionId, bool localScopeOnly)
        {
            if (localScopeOnly && (_parent != null))    //All options with valid definitions are set for the root.
                return OptionsSetLocally.Contains(optionId);

            EditorOptionDefinition definition = _factory.GetOptionDefinition(optionId);
            if ((definition != null) &&
                (Scope == null || definition.IsApplicableToScope(Scope)))
            {
                return true;
            }

            return false;
        }

        public bool IsOptionDefined<T>(EditorOptionKey<T> key, bool localScopeOnly)
        {
            if (localScopeOnly && (_parent != null))    //All options with valid definitions are set for the root.
            {
                return OptionsSetLocally.Contains(key.Name);
            }

            EditorOptionDefinition definition = _factory.GetOptionDefinition(key.Name);
            if ((definition != null) &&
                (Scope == null || definition.IsApplicableToScope(Scope)) &&
                definition.ValueType.IsEquivalentTo(typeof(T)))
            {
                return true;
            }

            return false;
        }

        public bool ClearOptionValue(string optionId)
        {
            if (this.Parent == null)
            {
                // Can't clear options on the Global options
                return false;
            }

            if (OptionsSetLocally.Contains(optionId))
            {
                object currentValue = OptionsSetLocally[optionId];

                OptionsSetLocally.Remove(optionId);

                EditorOptionDefinition definition = _factory.GetOptionDefinitionOrThrow(optionId);
                object inheritedValue =  this.GetOptionValue(definition);

                // See what the inherited option value was.  If it isn't changing,
                // then we don't need to raise an event.
                if (!object.Equals(currentValue, inheritedValue))
                {
                    RaiseChangedEvent(definition);
                }

                return true;
            }

            return false;
        }

        public bool ClearOptionValue<T>(EditorOptionKey<T> key)
        {
            return ClearOptionValue(key.Name);
        }

        public IEnumerable<EditorOptionDefinition> SupportedOptions
        {
            get
            {
                return _factory.GetSupportedOptions(this.Scope);
            }
        }

        public IEditorOptions GlobalOptions
        {
            get 
            { 
                return _factory.GlobalOptions;
            }
        }

        public event EventHandler<EditorOptionChangedEventArgs> OptionChanged;

        #endregion

        #region Private Helpers

        //A hook so we can tell whether or not the options have been hooked. Used only by unit tests.
        private object OptionChangedValue { get { return this.OptionChanged; } }

        private void RaiseChangedEvent(EditorOptionDefinition definition)
        {
            // First, send out local events, but only if the change is valid in this scope
            if (Scope == null || definition.IsApplicableToScope(Scope))
            {
                var tempEvent = OptionChanged;
                if (tempEvent != null)
                    tempEvent(this, new EditorOptionChangedEventArgs(definition.Name));
            }

            // Get rid of the expired refs
            DerivedEditorOptions.RemoveAll(weakref => !weakref.IsAlive);

            // Now, notify a copy of the derived options (since an event might modify the DerivedEditorOptions).
            foreach (var weakRef in new FrugalList<WeakReference>(DerivedEditorOptions))
            {
                EditorOptions derived = weakRef.Target as EditorOptions;
                if (derived != null)
                    derived.OnParentOptionChanged(definition);
            }
        }

        private void CheckForCycle()
        {
            EditorOptions parent = _parent;
            HashSet<EditorOptions> visited = new HashSet<EditorOptions>();

            while (parent != null)
            {
                if (visited.Contains(parent))
                    throw new ArgumentException("Cycles are not allowed in the Parent chain.");

                visited.Add(parent);
                parent = parent._parent;
            }
        }

        #endregion

        #region Internal "event" handling

        internal void AddDerivedOptions(EditorOptions derived)
        {
            // Get rid of the expired refs
            DerivedEditorOptions.RemoveAll(weakref => !weakref.IsAlive);

            DerivedEditorOptions.Add(new WeakReference(derived));
        }

        internal void RemovedDerivedOptions(EditorOptions derived)
        {
            foreach (var weakRef in DerivedEditorOptions)
            {
                if (weakRef.Target == derived)
                {
                    DerivedEditorOptions.Remove(weakRef);
                    break;
                }
            }
        }

        internal void OnParentOptionChanged(EditorOptionDefinition definition)
        {
            // We only notify if the given option isn't already set locally, since it
            // would be overriden by a parent option changing.
            if (!this.OptionsSetLocally.Contains(definition.Name))
                RaiseChangedEvent(definition);
        }

        #endregion

        private bool TryGetOption(EditorOptionDefinition definition, out object value)
        {
            value = null;

            if (Scope != null && !definition.IsApplicableToScope(Scope))
                return false;

            value = this.GetOptionForChild(definition);
            return true;
        }

        /// <summary>
        /// Get the given option from this (or its ancestors).  The caller should have already checked to ensure
        /// the given option is valid in the scope being requested.
        /// </summary>
        /// <param name="definition">Definition of the option to find.</param>
        /// <returns>The option's current value.</returns>
        internal object GetOptionForChild(EditorOptionDefinition definition)
        {
            if (OptionsSetLocally.Contains(definition.Name))
            {
                return OptionsSetLocally[definition.Name];
            }

            if (_parent == null)
            {
                return definition.DefaultValue;
            }

            return _parent.GetOptionForChild(definition);
        }
    }   
}
