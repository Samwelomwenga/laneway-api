# Laneway

A Trello-style board API. Work is organized as a tree: workspace, board, list, card, label.

## Language

**Workspace**:
The top-level container that groups boards.

**Board**:
A space inside a workspace that holds lists in order.

**List**:
An ordered column on a board that holds cards.

**Card**:
One unit of work, at a position in a list. When someone says "task", they mean a card.
_Avoid_: Task, item, ticket

**Position**:
Where a list sits on its board, or a card in its list. Only the order matters, not the number.
_Avoid_: Index, order, rank

**Move**:
Putting a card in another list, a list on another board, or a board in another workspace. A move to another board brings the card's labels along as that board's labels.
_Avoid_: Transfer

**Copy**:
A new card, list, or board made from an existing one, which stays unchanged. Comments, and archived items inside the original, never come along.
_Avoid_: Duplicate, clone

**Archived**:
A board, list, card, or checklist put away without being deleted, which can be restored. Archiving a board, list, card, or checklist hides what it holds but doesn't archive it. Nothing archived, and nothing an archived item holds, can be edited. A board, list, card, or checklist has to be archived before it can be deleted, and deleting it can't be undone. A workspace, label, or check item can't be archived.
_Avoid_: Closed

**Due date**:
When the work on a card should be finished. A card may have none.

**Complete**:
A due date marked as done. Only a card with a due date can be complete. A card with check items is complete exactly when every one of them is checked.
_Avoid_: Status, done

**Reminder**:
How long before a card's due date someone should be reminded. Only a card with a due date can have one. Nothing sends reminders yet.

**Label**:
A tag on one board, attached to that board's cards to group them. It has a name, a color, or both.
_Avoid_: Tag

**Checklist**:
A named, ordered list of check items on a card.

**Check item**:
One entry on a checklist, at a position within it.
_Avoid_: Item, checklist item, task

**Checked**:
A check item marked as done. When every check item on a card with a due date is checked, the card is complete.
_Avoid_: Complete, done, ticked

**Attachment**:
A file or a link added to a card. A file is a copy the app keeps. A link only points at a page somewhere else.
_Avoid_: Upload, asset, URL attachment

**Cover**:
The image or color shown on the front of a card. An image cover is always one of the card's own image attachments.
_Avoid_: Banner, thumbnail

### Activity

**Activity**:
The history of changes users made to a workspace, board, list, or card. Each change a user makes is one activity entry.
_Avoid_: Audit log, history, actions

**Activity entry**:
The record of one change a user made: who made it, when, and what changed. It stays after the item it describes is deleted.
_Avoid_: Action, event, log entry

**Comment**:
An activity entry holding text a user wrote on a card. Only the user who wrote it can edit or delete it, and no other activity entry can change.
_Avoid_: Note, reply

### People

**User**:
A person who works on boards.
_Avoid_: Member, account

**Actor**:
The user who makes a change. Every change has exactly one.
_Avoid_: Creator, current user

**Deleted user**:
A user who is gone for good. The work they did stays, and their activity entries still show their name, but the user can no longer be looked up.
_Avoid_: Removed user, inactive user, disabled user
