# Laneway

A kanban board API. Work is organized as a tree: workspace, board, list, card, label.

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
A board, list, card, or checklist put away without being deleted, which can be restored. Archiving a board, list, card, or checklist doesn't archive what it holds. It hides it from views above the archived item, so a card in an archived list doesn't show on its board. The archived item's own collection route still lists its unarchived children. Nothing archived, and nothing an archived item holds, can be edited. A board, list, card, or checklist has to be archived before it can be deleted, and deleting it can't be undone. A workspace, label, or check item can't be archived.
_Avoid_: Closed

**Open**:
A card or list that a board shows: not archived, and not hidden inside an archived list. A board holds at most 5,000 open cards and 500 open lists.
_Avoid_: Active, visible, unarchived

**Due date**:
When the work on a card should be finished. A card may have none.

**Complete**:
A due date marked as done. Only a card with a due date can be complete. A card with check items is complete exactly when every one of them is checked.
_Avoid_: Status, done

**Reminder**:
How long before a card's due date someone should be reminded. Only a card with a due date can have one. Nothing sends reminders yet.

**Label**:
A tag on one board, attached to that board's cards to group them. It has a name, a color, or both. A board holds at most 1,000 labels, and a card carries at most 100.
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
An activity entry holding text a user wrote on a card. Only the user who wrote it can edit or delete it, and only while they may still comment on its board. A board admin can delete any comment. No other activity entry can change.
_Avoid_: Note, reply

### People

**User**:
A person who works on boards.
_Avoid_: Account

**Member**:
A user who belongs to a workspace or a board, with one role there. A user can be a member of many places, with a different role in each.
_Avoid_: Collaborator, participant

**Role**:
What a member may do in the workspace or board they belong to. A workspace member is an admin or normal. A board member is an admin, normal, or an observer, who can read the board but not change it. The board settings decide whether observers may comment.
_Avoid_: User type, permission level, access level

**Guest**:
A board member who isn't a member of the board's workspace. Being a guest is a state, not a role: a guest is still an admin, normal, or observer on their board.
_Avoid_: External member, outside collaborator

**Former reader**:
A user who was a member of a workspace or board when it was deleted. A deleted Workspace or Public board also keeps every member of its workspace. Former readers keep reading its activity. Someone who joins the workspace later doesn't.
_Avoid_: Past member

**Visibility**:
Who can read a workspace or board without being its member. A board is Private, Workspace, or Public. A workspace is Private or Public. Public means any signed-in user.
_Avoid_: Privacy, access level, permission level

**Board settings**:
The rules a board admin sets for a board: who may comment on it, who may add members to it, whether workspace members may join it on their own, and its visibility.
_Avoid_: Prefs, permissions, board permissions

**Actor**:
The user who makes a change. Every change has exactly one.
_Avoid_: Creator, current user

**Deleted user**:
A user who is gone for good. The work they did stays, and their activity entries still show their name, but the user can no longer be looked up.
_Avoid_: Removed user, inactive user, disabled user

**Session**:
One stretch of a user being signed in on one device. It starts when the user signs in or uses their verification link. It ends when they sign out, sign out everywhere, or are deleted, when they change their password from another session, when their password is reset, or when it runs out. It also ends when the user ends it from their list of sessions, or when Laneway sees one of its old refresh tokens used again. A user can have several at once.
_Avoid_: Login, token family

**Password reset**:
Setting a new password through a link Laneway emails to a verified user who forgot theirs. The link works for 1 hour, and only the newest one works. A reset ends every session and any sign-in block, and signs nobody in.
_Avoid_: Password recovery

**Email change**:
Moving a user's sign-in address to a new one. The user asks with their password, and Laneway emails the new address a link that works for 24 hours. The address moves only when someone uses that link. Laneway then emails the old address with a link to undo the change for 7 days. A password reset cancels a pending email change, and a confirmed change voids any live reset link.
_Avoid_: Email update, email swap

**Password history**:
The hashes a user's password had before each change or reset, with when each was replaced. Laneway never shows it. It exists so undoing an email change can ask for a password the account used recently.
_Avoid_: Old passwords, previous passwords

**Profile**:
The signed-in user's own data, which only they can see or change. Other users see a smaller summary of it.
_Avoid_: Me, account

**Sign-in block**:
A stretch of time when a user can't sign in, because someone typed the wrong password for them too many times. Wrong passwords count the same whether they come from signing in, changing the password or email, or deleting the user, and a block stops all four. It ends on its own, and it gets longer the more wrong passwords keep coming. The user's existing sessions keep working.
_Avoid_: Lockout, locked account, suspended user

**Verified user**:
A user who has proved they own their email by using the link Laneway sent to it. Until then the user is unverified. An unverified user can't sign in or be added as a member. Someone else who signs up with the same email takes their place.
_Avoid_: Confirmed user, activated account, pending user
