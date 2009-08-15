﻿// <file>
//     <copyright see="prj:///doc/copyright.txt"/>
//     <license see="prj:///doc/license.txt"/>
//     <owner name="David Srbecký" email="dsrbecky@gmail.com"/>
//     <version>$Revision$</version>
// </file>

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;

using ICSharpCode.AvalonEdit.Document;

namespace ICSharpCode.AvalonEdit.Xml
{
	/// <summary>
	/// Abstact base class for all types that can contain child nodes
	/// </summary>
	public abstract class AXmlContainer: AXmlObject
	{
		/// <summary>
		/// Children of the node.  It is read-only.
		/// Note that is has CollectionChanged event.
		/// </summary>
		public AXmlObjectCollection<AXmlObject> Children { get; private set; }
		
		/// <summary> Create new container </summary>
		public AXmlContainer()
		{
			this.Children = new AXmlObjectCollection<AXmlObject>();
		}
		
		#region Helpper methods
		
		ObservableCollection<AXmlElement> elements;
		
		/// <summary> Gets direcly nested elements (non-recursive) </summary>
		public ObservableCollection<AXmlElement> Elements {
			get {
				if (elements == null) {
					elements = new FilteredCollection<AXmlElement, AXmlObjectCollection<AXmlObject>>(this.Children);
				}
				return elements;
			}
		}
		
		internal AXmlObject FirstChild {
			get {
				return this.Children[0];
			}
		}
		
		internal AXmlObject LastChild {
			get {
				return this.Children[this.Children.Count - 1];
			}
		}
		
		#endregion
		
		/// <inheritdoc/>
		public override IEnumerable<AXmlObject> GetSelfAndAllChildren()
		{
			return new AXmlObject[] { this }.Flatten(i => i is AXmlContainer ? ((AXmlContainer)i).Children : null);
		}
		
		/// <summary>
		/// Gets a child fully containg the given offset.
		/// Goes recursively down the tree.
		/// Specail case if at the end of attribute or text
		/// </summary>
		public AXmlObject GetChildAtOffset(int offset)
		{
			foreach(AXmlObject child in this.Children) {
				if ((child is AXmlAttribute || child is AXmlText) && offset == child.EndOffset) return child;
				if (child.StartOffset < offset && offset < child.EndOffset) {
					if (child is AXmlContainer) {
						return ((AXmlContainer)child).GetChildAtOffset(offset);
					} else {
						return child;
					}
				}
			}
			return this; // No childs at offset
		}
		
		// Only these four methods should be used to modify the collection
		
		/// <summary> To be used exlucively by the parser </summary>
		internal void AddChild(AXmlObject item)
		{
			// Childs can be only added to newly parsed items
			Assert(this.Parent == null, "I have to be new");
			Assert(item.IsCached, "Added item must be in cache");
			// Do not set parent pointer
			this.Children.InsertItemAt(this.Children.Count, item);
		}
		
		/// <summary> To be used exlucively by the parser </summary>
		internal void AddChildren(IEnumerable<AXmlObject> items)
		{
			// Childs can be only added to newly parsed items
			Assert(this.Parent == null, "I have to be new");
			// Do not set parent pointer
			this.Children.InsertItemsAt(this.Children.Count, items.ToList());
		}
		
		/// <summary>
		/// To be used exclusively by the children update algorithm.
		/// Insert child and keep links consistent.
		/// </summary>
		void InsertChild(int index, AXmlObject item)
		{
			AXmlParser.Log("Inserting {0} at index {1}", item, index);
			
			AXmlDocument document = this.Document;
			Assert(document != null, "Can not insert to dangling object");
			Assert(item.Parent != this, "Can not own item twice");
			
			SetParentPointersInTree(item);
			
			this.Children.InsertItemAt(index, item);
			
			document.OnObjectInserted(index, item);
		}
		
		/// <summary> Recursively fix all parent pointer in a tree </summary>
		/// <remarks>
		/// Cache constraint:
		///    If cached item has parent set, then the whole subtree must be consistent
		/// </remarks>
		void SetParentPointersInTree(AXmlObject item)
		{
			// All items come from the parser cache
			
			if (item.Parent == null) {
				// Dangling object - either a new parser object or removed tree (still cached)
				item.Parent = this;
				if (item is AXmlContainer) {
					foreach(AXmlObject child in ((AXmlContainer)item).Children) {
						((AXmlContainer)item).SetParentPointersInTree(child);
					}
				}
			} else if (item.Parent == this) {
				// If node is attached and then deattached, it will have null parent pointer
				//   but valid subtree - so its children will alredy have correct parent pointer
				//   like in this case
				item.DebugCheckConsistency(false);
				// Rest of the tree is consistent - do not recurse
			} else {
				// From cache & parent set => consitent subtree
				item.DebugCheckConsistency(false);
				// The parent (or any futher parents) can not be part of parsed document
				//   becuase otherwise this item would be included twice => safe to change parents
				DebugAssert(item.Parent.Document == null, "Old parent is part of document as well");
				// Maintain cache constraint by setting parents to null
				foreach(AXmlObject ancest in item.GetAncestors().ToList()) {
					ancest.Parent = null; 
				}
				item.Parent = this;
				// Rest of the tree is consistent - do not recurse
			}
		}
		
		/// <summary>
		/// To be used exclusively by the children update algorithm.
		/// Remove child, set parent to null and notify the document
		/// </summary>
		void RemoveChild(int index)
		{
			AXmlObject removed = this.Children[index];
			AXmlParser.Log("Removing {0} at index {1}", removed, index);
			
			// Null parent pointer
			Assert(removed.Parent == this, "Inconsistent child");
			removed.Parent = null;
			
			this.Children.RemoveItemAt(index);
			
			this.Document.OnObjectRemoved(index, removed);
		}
		
		/// <summary> Verify that the subtree is consistent.  Only in debug build. </summary>
		/// <remarks> Parent pointers might be null or pointing somewhere else in parse tree </remarks>
		internal override void DebugCheckConsistency(bool checkParentPointers)
		{
			base.DebugCheckConsistency(checkParentPointers);
			AXmlObject prevChild = null;
			int myStartOffset = this.StartOffset;
			int myEndOffset = this.EndOffset;
			foreach(AXmlObject child in this.Children) {
				Assert(child.Length != 0, "Empty child");
				if (checkParentPointers) {
					Assert(child.Parent != null, "Null parent reference");
					Assert(child.Parent == this, "Inccorect parent reference");
				}
				Assert(myStartOffset <= child.StartOffset && child.EndOffset <= myEndOffset, "Child not within parent text range");
				if (this.IsCached)
					Assert(child.IsCached, "Child not in cache");
				if (prevChild != null)
					Assert(prevChild.EndOffset <= child.StartOffset, "Overlaping childs");
				child.DebugCheckConsistency(checkParentPointers);
				prevChild = child;
			}
		}
		
		/// <remarks>
		/// Note the the method is not called recuively.
		/// Only the helper methods are recursive.
		/// </remarks>
		internal void UpdateTreeFrom(AXmlContainer srcContainer)
		{
			this.UpdateDataFrom(srcContainer);
			RemoveChildrenNotIn(srcContainer.Children);
			InsertAndUpdateChildrenFrom(srcContainer.Children);
		}
		
		void RemoveChildrenNotIn(IList<AXmlObject> srcList)
		{
			Dictionary<int, AXmlObject> srcChildren = srcList.ToDictionary(i => i.StartOffset);
			for(int i = 0; i < this.Children.Count;) {
				AXmlObject child = this.Children[i];
				AXmlObject srcChild;
				
				if (srcChildren.TryGetValue(child.StartOffset, out srcChild) && child.CanUpdateDataFrom(srcChild)) {
					// Keep only one item with given offset (we might have several due to deletion)
					srcChildren.Remove(child.StartOffset);
					if (child is AXmlContainer)
						((AXmlContainer)child).RemoveChildrenNotIn(((AXmlContainer)srcChild).Children);
					i++;
				} else {
					RemoveChild(i);
				}
			}
		}
		
		void InsertAndUpdateChildrenFrom(IList<AXmlObject> srcList)
		{
			for(int i = 0; i < srcList.Count; i++) {
				// End of our list?
				if (i == this.Children.Count) {
					InsertChild(i, srcList[i]);
					continue;
				}
				AXmlObject child = this.Children[i];
				AXmlObject srcChild = srcList[i];
				
				if (child.CanUpdateDataFrom(srcChild) /* includes offset test */) {
					child.UpdateDataFrom(srcChild);
					if (child is AXmlContainer)
						((AXmlContainer)child).InsertAndUpdateChildrenFrom(((AXmlContainer)srcChild).Children);
				} else {
					InsertChild(i, srcChild);
				}
			}
			Assert(this.Children.Count == srcList.Count, "List lengths differ after update");
		}
	}
}