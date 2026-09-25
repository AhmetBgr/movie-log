-- Movie Log: Supabase schema for library sync.
-- Run once in the Supabase dashboard: SQL Editor -> New query -> paste -> Run.
-- Safe to re-run.

-- One row per synced record: kind = 'item' (key = IMDb id) or 'collection' (key = collection GUID).
-- Deleted records are kept as tombstones (deleted = true, data = null) so other devices can apply the delete.
create table if not exists public.library_records (
  user_id    uuid        not null default auth.uid() references auth.users (id) on delete cascade,
  kind       text        not null check (kind in ('item', 'collection')),
  key        text        not null,
  data       jsonb,
  hash       text,
  deleted    boolean     not null default false,
  updated_at timestamptz not null default now(),
  primary key (user_id, kind, key)
);

alter table public.library_records enable row level security;

drop policy if exists "Users manage their own library records" on public.library_records;
create policy "Users manage their own library records"
  on public.library_records
  for all
  to authenticated
  using ((select auth.uid()) = user_id)
  with check ((select auth.uid()) = user_id);

grant select, insert, update, delete on public.library_records to authenticated;

-- Keep updated_at current on every change.
create or replace function public.library_records_touch()
returns trigger
language plpgsql
set search_path = ''
as $$
begin
  new.updated_at = now();
  return new;
end;
$$;

drop trigger if exists library_records_touch on public.library_records;
create trigger library_records_touch
  before update on public.library_records
  for each row execute function public.library_records_touch();
