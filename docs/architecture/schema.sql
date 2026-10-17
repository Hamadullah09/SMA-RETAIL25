/* Retail25 -- schema reference for the "retail25" database.

   Generated 2026-10-17 with SMO, from a database carrying every applied migration.
   Schema only: this file contains no rows.

   It is a reference, not an installer. A fresh database is created by running the
   API with Database:AutoMigrate set, so that EF writes the __EFMigrationsHistory
   rows it later checks against; a database built by running this file would have
   the right tables and no migration history, and could never be upgraded.
   docs/runbooks/database-from-scratch.md has the procedure. */


/* ====================================================================================
   SCHEMAS  (1)
   ==================================================================================== */

CREATE SCHEMA [HangFire]
GO

/* ====================================================================================
   SEQUENCES  (4)
   ==================================================================================== */

CREATE SEQUENCE [dbo].[seq_customer_1] 
 AS [bigint]
 START WITH 1
 INCREMENT BY 1
 MINVALUE -9223372036854775808
 MAXVALUE 9223372036854775807
 CACHE 
GO
CREATE SEQUENCE [dbo].[seq_customerorder_1] 
 AS [bigint]
 START WITH 1
 INCREMENT BY 1
 MINVALUE -9223372036854775808
 MAXVALUE 9223372036854775807
 CACHE 
GO
CREATE SEQUENCE [dbo].[seq_layaway_1] 
 AS [bigint]
 START WITH 1
 INCREMENT BY 1
 MINVALUE -9223372036854775808
 MAXVALUE 9223372036854775807
 CACHE 
GO
CREATE SEQUENCE [dbo].[seq_transaction_1] 
 AS [bigint]
 START WITH 1
 INCREMENT BY 1
 MINVALUE -9223372036854775808
 MAXVALUE 9223372036854775807
 CACHE 
GO

/* ====================================================================================
   TABLES, WITH THEIR KEYS, CONSTRAINTS AND INDEXES  (116)
   ==================================================================================== */

SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[__EFMigrationsHistory](
	[migration_id] [nvarchar](150) NOT NULL,
	[product_version] [nvarchar](32) NOT NULL,
 CONSTRAINT [pk___ef_migrations_history] PRIMARY KEY CLUSTERED 
(
	[migration_id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[ar_ledger_entries](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[customer_id] [bigint] NOT NULL,
	[invoice_id] [bigint] NOT NULL,
	[entry_type] [int] NOT NULL,
	[amount] [decimal](19, 4) NOT NULL,
	[occurred_at] [datetimeoffset](7) NOT NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_ar_ledger_entries] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[AspNetRoleClaims](
	[id] [int] IDENTITY(1,1) NOT NULL,
	[role_id] [bigint] NOT NULL,
	[claim_type] [nvarchar](max) NULL,
	[claim_value] [nvarchar](max) NULL,
 CONSTRAINT [pk_asp_net_role_claims] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

CREATE NONCLUSTERED INDEX [ix_asp_net_role_claims_role_id] ON [dbo].[AspNetRoleClaims]
(
	[role_id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
ALTER TABLE [dbo].[AspNetRoleClaims]  WITH CHECK ADD  CONSTRAINT [fk_asp_net_role_claims_asp_net_roles_role_id] FOREIGN KEY([role_id])
REFERENCES [dbo].[AspNetRoles] ([id])
ON DELETE CASCADE
ALTER TABLE [dbo].[AspNetRoleClaims] CHECK CONSTRAINT [fk_asp_net_role_claims_asp_net_roles_role_id]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[AspNetRoles](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[legacy_level] [int] NULL,
	[description] [nvarchar](max) NULL,
	[name] [nvarchar](256) NULL,
	[normalized_name] [nvarchar](256) NULL,
	[concurrency_stamp] [nvarchar](max) NULL,
 CONSTRAINT [pk_asp_net_roles] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

SET ANSI_PADDING ON

CREATE UNIQUE NONCLUSTERED INDEX [RoleNameIndex] ON [dbo].[AspNetRoles]
(
	[normalized_name] ASC
)
WHERE ([normalized_name] IS NOT NULL)
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[AspNetUserClaims](
	[id] [int] IDENTITY(1,1) NOT NULL,
	[user_id] [bigint] NOT NULL,
	[claim_type] [nvarchar](max) NULL,
	[claim_value] [nvarchar](max) NULL,
 CONSTRAINT [pk_asp_net_user_claims] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

CREATE NONCLUSTERED INDEX [ix_asp_net_user_claims_user_id] ON [dbo].[AspNetUserClaims]
(
	[user_id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
ALTER TABLE [dbo].[AspNetUserClaims]  WITH CHECK ADD  CONSTRAINT [fk_asp_net_user_claims_asp_net_users_user_id] FOREIGN KEY([user_id])
REFERENCES [dbo].[AspNetUsers] ([id])
ON DELETE CASCADE
ALTER TABLE [dbo].[AspNetUserClaims] CHECK CONSTRAINT [fk_asp_net_user_claims_asp_net_users_user_id]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[AspNetUserLogins](
	[login_provider] [nvarchar](450) NOT NULL,
	[provider_key] [nvarchar](450) NOT NULL,
	[provider_display_name] [nvarchar](max) NULL,
	[user_id] [bigint] NOT NULL,
 CONSTRAINT [pk_asp_net_user_logins] PRIMARY KEY CLUSTERED 
(
	[login_provider] ASC,
	[provider_key] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

CREATE NONCLUSTERED INDEX [ix_asp_net_user_logins_user_id] ON [dbo].[AspNetUserLogins]
(
	[user_id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
ALTER TABLE [dbo].[AspNetUserLogins]  WITH CHECK ADD  CONSTRAINT [fk_asp_net_user_logins_asp_net_users_user_id] FOREIGN KEY([user_id])
REFERENCES [dbo].[AspNetUsers] ([id])
ON DELETE CASCADE
ALTER TABLE [dbo].[AspNetUserLogins] CHECK CONSTRAINT [fk_asp_net_user_logins_asp_net_users_user_id]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[AspNetUserRoles](
	[user_id] [bigint] NOT NULL,
	[role_id] [bigint] NOT NULL,
 CONSTRAINT [pk_asp_net_user_roles] PRIMARY KEY CLUSTERED 
(
	[user_id] ASC,
	[role_id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

CREATE NONCLUSTERED INDEX [ix_asp_net_user_roles_role_id] ON [dbo].[AspNetUserRoles]
(
	[role_id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
ALTER TABLE [dbo].[AspNetUserRoles]  WITH CHECK ADD  CONSTRAINT [fk_asp_net_user_roles_asp_net_roles_role_id] FOREIGN KEY([role_id])
REFERENCES [dbo].[AspNetRoles] ([id])
ON DELETE CASCADE
ALTER TABLE [dbo].[AspNetUserRoles] CHECK CONSTRAINT [fk_asp_net_user_roles_asp_net_roles_role_id]
ALTER TABLE [dbo].[AspNetUserRoles]  WITH CHECK ADD  CONSTRAINT [fk_asp_net_user_roles_asp_net_users_user_id] FOREIGN KEY([user_id])
REFERENCES [dbo].[AspNetUsers] ([id])
ON DELETE CASCADE
ALTER TABLE [dbo].[AspNetUserRoles] CHECK CONSTRAINT [fk_asp_net_user_roles_asp_net_users_user_id]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[AspNetUsers](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[display_name] [nvarchar](max) NOT NULL,
	[is_enabled] [bit] NOT NULL,
	[default_location_id] [bigint] NULL,
	[last_signed_in_at] [datetimeoffset](7) NULL,
	[user_name] [nvarchar](256) NULL,
	[normalized_user_name] [nvarchar](256) NULL,
	[email] [nvarchar](256) NULL,
	[normalized_email] [nvarchar](256) NULL,
	[email_confirmed] [bit] NOT NULL,
	[password_hash] [nvarchar](max) NULL,
	[security_stamp] [nvarchar](max) NULL,
	[concurrency_stamp] [nvarchar](max) NULL,
	[phone_number] [nvarchar](max) NULL,
	[phone_number_confirmed] [bit] NOT NULL,
	[two_factor_enabled] [bit] NOT NULL,
	[lockout_end] [datetimeoffset](7) NULL,
	[lockout_enabled] [bit] NOT NULL,
	[access_failed_count] [int] NOT NULL,
 CONSTRAINT [pk_asp_net_users] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

SET ANSI_PADDING ON

CREATE NONCLUSTERED INDEX [EmailIndex] ON [dbo].[AspNetUsers]
(
	[normalized_email] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
SET ANSI_PADDING ON

CREATE UNIQUE NONCLUSTERED INDEX [UserNameIndex] ON [dbo].[AspNetUsers]
(
	[normalized_user_name] ASC
)
WHERE ([normalized_user_name] IS NOT NULL)
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[AspNetUserTokens](
	[user_id] [bigint] NOT NULL,
	[login_provider] [nvarchar](450) NOT NULL,
	[name] [nvarchar](450) NOT NULL,
	[value] [nvarchar](max) NULL,
 CONSTRAINT [pk_asp_net_user_tokens] PRIMARY KEY CLUSTERED 
(
	[user_id] ASC,
	[login_provider] ASC,
	[name] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

ALTER TABLE [dbo].[AspNetUserTokens]  WITH CHECK ADD  CONSTRAINT [fk_asp_net_user_tokens_asp_net_users_user_id] FOREIGN KEY([user_id])
REFERENCES [dbo].[AspNetUsers] ([id])
ON DELETE CASCADE
ALTER TABLE [dbo].[AspNetUserTokens] CHECK CONSTRAINT [fk_asp_net_user_tokens_asp_net_users_user_id]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[audit_log_entries](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[occurred_at] [datetimeoffset](7) NOT NULL,
	[action] [nvarchar](24) NOT NULL,
	[actor_user_id] [bigint] NULL,
	[actor_staff_id] [bigint] NULL,
	[actor_name] [nvarchar](200) NULL,
	[station_id] [bigint] NULL,
	[location_id] [bigint] NULL,
	[ip_address] [nvarchar](64) NULL,
	[entity_type] [nvarchar](80) NOT NULL,
	[entity_id] [nvarchar](64) NULL,
	[operation] [nvarchar](120) NULL,
	[before_json] [nvarchar](max) NULL,
	[after_json] [nvarchar](max) NULL,
	[correlation_id] [nvarchar](128) NULL,
	[approver_staff_id] [bigint] NULL,
	[reason] [nvarchar](500) NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_audit_log_entries] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

CREATE NONCLUSTERED INDEX [ix_audit_log_entries_actor_staff_id_occurred_at] ON [dbo].[audit_log_entries]
(
	[actor_staff_id] ASC,
	[occurred_at] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
SET ANSI_PADDING ON

CREATE NONCLUSTERED INDEX [ix_audit_log_entries_correlation_id] ON [dbo].[audit_log_entries]
(
	[correlation_id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
SET ANSI_PADDING ON

CREATE NONCLUSTERED INDEX [ix_audit_log_entries_entity_type_entity_id] ON [dbo].[audit_log_entries]
(
	[entity_type] ASC,
	[entity_id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
CREATE NONCLUSTERED INDEX [ix_audit_log_entries_occurred_at] ON [dbo].[audit_log_entries]
(
	[occurred_at] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[bonus_pricings](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[product_id] [bigint] NOT NULL,
	[buy_qty] [decimal](19, 4) NOT NULL,
	[free_qty] [decimal](19, 4) NOT NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_bonus_pricings] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[branding_assets](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[location_id] [bigint] NOT NULL,
	[slot] [nvarchar](20) NOT NULL,
	[content] [varbinary](max) NOT NULL,
	[content_type] [nvarchar](40) NOT NULL,
	[e_tag] [nvarchar](32) NOT NULL,
	[opacity_pct] [int] NOT NULL,
	[created_at] [datetimeoffset](7) NOT NULL,
	[created_by] [bigint] NULL,
	[modified_at] [datetimeoffset](7) NULL,
	[modified_by] [bigint] NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_branding_assets] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

SET ANSI_PADDING ON

CREATE UNIQUE NONCLUSTERED INDEX [ix_branding_assets_location_id_slot] ON [dbo].[branding_assets]
(
	[location_id] ASC,
	[slot] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
ALTER TABLE [dbo].[branding_assets]  WITH CHECK ADD  CONSTRAINT [fk_branding_assets_locations_location_id] FOREIGN KEY([location_id])
REFERENCES [dbo].[locations] ([id])
ON DELETE CASCADE
ALTER TABLE [dbo].[branding_assets] CHECK CONSTRAINT [fk_branding_assets_locations_location_id]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[business_profiles](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[location_id] [bigint] NOT NULL,
	[business_name] [nvarchar](200) NOT NULL,
	[address_line1] [nvarchar](200) NULL,
	[address_line2] [nvarchar](200) NULL,
	[address_city] [nvarchar](100) NULL,
	[address_state_or_province] [nvarchar](100) NULL,
	[address_postal_code] [nvarchar](20) NULL,
	[address_country] [nvarchar](100) NULL,
	[contact_phone] [nvarchar](30) NULL,
	[contact_extension] [nvarchar](10) NULL,
	[contact_mobile] [nvarchar](30) NULL,
	[contact_fax] [nvarchar](30) NULL,
	[contact_email] [nvarchar](200) NULL,
	[contact_website] [nvarchar](200) NULL,
	[licence_number] [nvarchar](60) NULL,
	[tax_registration_number] [nvarchar](40) NULL,
	[created_at] [datetimeoffset](7) NOT NULL,
	[created_by] [bigint] NULL,
	[modified_at] [datetimeoffset](7) NULL,
	[modified_by] [bigint] NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_business_profiles] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

CREATE UNIQUE NONCLUSTERED INDEX [ix_business_profiles_location_id] ON [dbo].[business_profiles]
(
	[location_id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[cached_cart](
	[cart_id] [bigint] NOT NULL,
	[station_id] [bigint] NOT NULL,
	[is_active] [bit] NOT NULL,
	[payload] [nvarchar](max) NOT NULL,
	[expires_at] [datetimeoffset](7) NOT NULL,
	[saved_at] [datetimeoffset](7) NOT NULL,
 CONSTRAINT [pk_cached_cart] PRIMARY KEY CLUSTERED 
(
	[cart_id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

CREATE NONCLUSTERED INDEX [ix_cached_cart_expires_at] ON [dbo].[cached_cart]
(
	[expires_at] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
CREATE NONCLUSTERED INDEX [ix_cached_cart_station_active] ON [dbo].[cached_cart]
(
	[station_id] ASC,
	[saved_at] ASC
)
WHERE ([is_active]=(1))
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[cached_hub_ticket](
	[ticket] [nvarchar](128) NOT NULL,
	[payload] [nvarchar](max) NOT NULL,
	[expires_at] [datetimeoffset](7) NOT NULL,
	[redemptions_remaining] [int] NOT NULL,
 CONSTRAINT [pk_cached_hub_ticket] PRIMARY KEY CLUSTERED 
(
	[ticket] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

CREATE NONCLUSTERED INDEX [ix_cached_hub_ticket_expires_at] ON [dbo].[cached_hub_ticket]
(
	[expires_at] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
ALTER TABLE [dbo].[cached_hub_ticket] ADD  DEFAULT ((2)) FOR [redemptions_remaining]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[cached_idempotency_entry](
	[idempotency_key] [nvarchar](256) NOT NULL,
	[payload] [nvarchar](max) NOT NULL,
	[expires_at] [datetimeoffset](7) NOT NULL,
 CONSTRAINT [pk_cached_idempotency_entry] PRIMARY KEY CLUSTERED 
(
	[idempotency_key] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

CREATE NONCLUSTERED INDEX [ix_cached_idempotency_entry_expires_at] ON [dbo].[cached_idempotency_entry]
(
	[expires_at] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[cached_tag_claim](
	[epc] [nvarchar](128) NOT NULL,
	[station_id] [bigint] NOT NULL,
	[expires_at] [datetimeoffset](7) NOT NULL,
 CONSTRAINT [pk_cached_tag_claim] PRIMARY KEY CLUSTERED 
(
	[epc] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

CREATE NONCLUSTERED INDEX [ix_cached_tag_claim_expires_at] ON [dbo].[cached_tag_claim]
(
	[expires_at] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[cart_adjustments](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[cart_id] [bigint] NOT NULL,
	[type] [nvarchar](24) NOT NULL,
	[label] [nvarchar](120) NOT NULL,
	[amount] [decimal](19, 4) NOT NULL,
	[percent] [decimal](7, 2) NOT NULL,
	[serial] [nvarchar](64) NULL,
	[applied_by_staff_id] [bigint] NOT NULL,
	[applied_at] [datetimeoffset](7) NOT NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_cart_adjustments] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

CREATE NONCLUSTERED INDEX [ix_cart_adjustments_cart_id] ON [dbo].[cart_adjustments]
(
	[cart_id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[cart_lines](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[cart_id] [bigint] NOT NULL,
	[product_id] [bigint] NOT NULL,
	[variant_id] [bigint] NULL,
	[serialized_unit_id] [bigint] NULL,
	[epc] [nvarchar](max) NULL,
	[source] [nvarchar](20) NOT NULL,
	[quantity] [decimal](18, 4) NOT NULL,
	[manual_unit_price] [decimal](19, 4) NULL,
	[manual_discount_pct] [decimal](19, 4) NULL,
	[requested_price_level] [int] NULL,
	[tax1override] [bit] NULL,
	[tax2override] [bit] NULL,
	[embedded_price] [decimal](19, 4) NULL,
	[line_type] [nvarchar](20) NOT NULL,
	[return_to_stock] [bit] NOT NULL,
	[note] [nvarchar](max) NULL,
	[sequence] [int] NOT NULL,
	[unit_price] [decimal](19, 4) NOT NULL,
	[price_origin] [nvarchar](20) NOT NULL,
	[line_discount_pct] [decimal](7, 2) NOT NULL,
	[tax1applies] [bit] NOT NULL,
	[tax2applies] [bit] NOT NULL,
	[extended_net] [decimal](19, 4) NOT NULL,
	[tax1amount] [decimal](19, 4) NOT NULL,
	[tax2amount] [decimal](19, 4) NOT NULL,
	[stock_code_snapshot] [nvarchar](24) NULL,
	[name_snapshot] [nvarchar](200) NULL,
	[unit_cost_snapshot] [decimal](19, 3) NOT NULL,
	[row_version] [bigint] NOT NULL,
	[unit_weight_snapshot] [decimal](19, 4) NOT NULL,
 CONSTRAINT [pk_cart_lines] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

CREATE NONCLUSTERED INDEX [ix_cart_lines_cart_id] ON [dbo].[cart_lines]
(
	[cart_id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
ALTER TABLE [dbo].[cart_lines] ADD  DEFAULT ((0.0)) FOR [unit_weight_snapshot]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[cart_tax_overrides](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[cart_id] [bigint] NOT NULL,
	[tax1] [bit] NULL,
	[tax2] [bit] NULL,
	[applies_from_sequence] [int] NOT NULL,
	[applied_by_staff_id] [bigint] NOT NULL,
	[applied_at] [datetimeoffset](7) NOT NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_cart_tax_overrides] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

CREATE UNIQUE NONCLUSTERED INDEX [ix_cart_tax_overrides_cart_id] ON [dbo].[cart_tax_overrides]
(
	[cart_id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[carts](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[station_id] [bigint] NOT NULL,
	[location_id] [bigint] NOT NULL,
	[staff_id] [bigint] NOT NULL,
	[customer_id] [bigint] NULL,
	[status] [nvarchar](20) NOT NULL,
	[held_name] [nvarchar](100) NULL,
	[suspended_at] [datetimeoffset](7) NULL,
	[suspended_by_staff_id] [bigint] NULL,
	[next_line_sequence] [int] NOT NULL,
	[revision] [int] NOT NULL,
	[completed_transaction_id] [bigint] NULL,
	[created_at] [datetimeoffset](7) NOT NULL,
	[created_by] [bigint] NULL,
	[modified_at] [datetimeoffset](7) NULL,
	[modified_by] [bigint] NULL,
	[expires_at] [datetimeoffset](7) NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_carts] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

SET ANSI_PADDING ON

CREATE NONCLUSTERED INDEX [ix_carts_station_id_status] ON [dbo].[carts]
(
	[station_id] ASC,
	[status] ASC
)
WHERE ([status]='Active')
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[categories](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[location_id] [bigint] NOT NULL,
	[name] [nvarchar](max) NOT NULL,
	[code] [nvarchar](max) NULL,
	[sort_order] [int] NOT NULL,
	[is_active] [bit] NOT NULL,
	[is_deleted] [bit] NOT NULL,
	[deleted_at] [datetimeoffset](7) NULL,
	[deleted_by] [bigint] NULL,
	[created_at] [datetimeoffset](7) NOT NULL,
	[created_by] [bigint] NULL,
	[modified_at] [datetimeoffset](7) NULL,
	[modified_by] [bigint] NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_categories] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[commission_ledger_entries](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[staff_id] [bigint] NOT NULL,
	[location_id] [bigint] NOT NULL,
	[transaction_id] [bigint] NOT NULL,
	[sale_line_id] [bigint] NOT NULL,
	[product_id] [bigint] NOT NULL,
	[stock_code_snapshot] [nvarchar](30) NOT NULL,
	[department_id] [bigint] NULL,
	[commission_rule_id] [bigint] NULL,
	[commission_type] [nvarchar](20) NOT NULL,
	[rate_applied] [decimal](9, 4) NOT NULL,
	[line_net] [decimal](19, 2) NOT NULL,
	[line_cost] [decimal](19, 3) NOT NULL,
	[quantity] [decimal](18, 4) NOT NULL,
	[amount] [decimal](19, 2) NOT NULL,
	[was_capped] [bit] NOT NULL,
	[business_date] [date] NOT NULL,
	[occurred_at] [datetimeoffset](7) NOT NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_commission_ledger_entries] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

CREATE NONCLUSTERED INDEX [ix_commission_ledger_entries_location_id_business_date] ON [dbo].[commission_ledger_entries]
(
	[location_id] ASC,
	[business_date] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
CREATE NONCLUSTERED INDEX [ix_commission_ledger_entries_staff_id_business_date] ON [dbo].[commission_ledger_entries]
(
	[staff_id] ASC,
	[business_date] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
CREATE NONCLUSTERED INDEX [ix_commission_ledger_entries_transaction_id] ON [dbo].[commission_ledger_entries]
(
	[transaction_id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[commission_rules](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[staff_id] [bigint] NOT NULL,
	[product_id] [bigint] NULL,
	[department_id] [bigint] NULL,
	[commission_type] [nvarchar](20) NOT NULL,
	[value] [decimal](9, 4) NOT NULL,
	[max_commission] [decimal](19, 2) NULL,
	[is_active] [bit] NOT NULL,
	[created_at] [datetimeoffset](7) NOT NULL,
	[created_by] [bigint] NULL,
	[modified_at] [datetimeoffset](7) NULL,
	[modified_by] [bigint] NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_commission_rules] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

CREATE NONCLUSTERED INDEX [ix_commission_rules_staff_id] ON [dbo].[commission_rules]
(
	[staff_id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
CREATE UNIQUE NONCLUSTERED INDEX [ix_commission_rules_staff_id_product_id_department_id] ON [dbo].[commission_rules]
(
	[staff_id] ASC,
	[product_id] ASC,
	[department_id] ASC
)
WHERE ([product_id] IS NOT NULL AND [department_id] IS NOT NULL)
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[currencies](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[code] [nvarchar](max) NOT NULL,
	[name] [nvarchar](max) NOT NULL,
	[symbol] [nvarchar](max) NOT NULL,
	[scale] [int] NOT NULL,
	[rounding] [int] NOT NULL,
	[minimum_tender] [decimal](19, 4) NOT NULL,
	[is_base_currency] [bit] NOT NULL,
	[is_active] [bit] NOT NULL,
	[exchange_rate] [decimal](19, 4) NOT NULL,
	[exchange_rate_updated_at] [datetimeoffset](7) NULL,
	[created_at] [datetimeoffset](7) NOT NULL,
	[created_by] [bigint] NULL,
	[modified_at] [datetimeoffset](7) NULL,
	[modified_by] [bigint] NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_currencies] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[customer_accounts](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[customer_id] [bigint] NOT NULL,
	[account_number] [bigint] NOT NULL,
	[credit_limit] [decimal](19, 4) NOT NULL,
	[balance_due] [decimal](19, 4) NOT NULL,
	[created_at] [datetimeoffset](7) NOT NULL,
	[created_by] [bigint] NULL,
	[modified_at] [datetimeoffset](7) NULL,
	[modified_by] [bigint] NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_customer_accounts] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[customer_order_lines](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[customer_order_id] [bigint] NOT NULL,
	[product_id] [bigint] NOT NULL,
	[variant_id] [bigint] NULL,
	[ordered_qty] [decimal](19, 4) NOT NULL,
	[filled_qty] [decimal](19, 4) NOT NULL,
	[unit_price] [decimal](19, 4) NOT NULL,
	[created_at] [datetimeoffset](7) NOT NULL,
	[created_by] [bigint] NULL,
	[modified_at] [datetimeoffset](7) NULL,
	[modified_by] [bigint] NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_customer_order_lines] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[customer_orders](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[order_number] [bigint] NOT NULL,
	[customer_id] [bigint] NOT NULL,
	[location_id] [bigint] NOT NULL,
	[status] [int] NOT NULL,
	[ordered_on] [date] NOT NULL,
	[notes] [nvarchar](max) NULL,
	[staff_id] [bigint] NOT NULL,
	[created_at] [datetimeoffset](7) NOT NULL,
	[created_by] [bigint] NULL,
	[modified_at] [datetimeoffset](7) NULL,
	[modified_by] [bigint] NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_customer_orders] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[customer_pricing_profiles](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[customer_id] [bigint] NOT NULL,
	[usual_discount_pct] [decimal](19, 4) NOT NULL,
	[price_level] [int] NOT NULL,
	[exempt_tax1] [bit] NOT NULL,
	[exempt_tax2] [bit] NOT NULL,
	[reward_points] [int] NOT NULL,
	[created_at] [datetimeoffset](7) NOT NULL,
	[created_by] [bigint] NULL,
	[modified_at] [datetimeoffset](7) NULL,
	[modified_by] [bigint] NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_customer_pricing_profiles] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[customers](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[location_id] [bigint] NOT NULL,
	[customer_number] [bigint] NOT NULL,
	[first_name] [nvarchar](100) NOT NULL,
	[last_name] [nvarchar](100) NOT NULL,
	[company] [nvarchar](200) NULL,
	[title] [nvarchar](50) NULL,
	[billing_address_line1] [nvarchar](200) NULL,
	[billing_address_line2] [nvarchar](200) NULL,
	[billing_address_city] [nvarchar](100) NULL,
	[billing_address_state_or_province] [nvarchar](100) NULL,
	[billing_address_postal_code] [nvarchar](20) NULL,
	[billing_address_country] [nvarchar](100) NULL,
	[ship_to_address_line1] [nvarchar](200) NULL,
	[ship_to_address_line2] [nvarchar](200) NULL,
	[ship_to_address_city] [nvarchar](100) NULL,
	[ship_to_address_state_or_province] [nvarchar](100) NULL,
	[ship_to_address_postal_code] [nvarchar](20) NULL,
	[ship_to_address_country] [nvarchar](100) NULL,
	[contact_phone] [nvarchar](30) NULL,
	[contact_extension] [nvarchar](10) NULL,
	[contact_mobile] [nvarchar](30) NULL,
	[contact_fax] [nvarchar](30) NULL,
	[contact_email] [nvarchar](200) NULL,
	[contact_website] [nvarchar](200) NULL,
	[client_type] [nvarchar](50) NULL,
	[birthday] [date] NULL,
	[notes] [nvarchar](4000) NULL,
	[last_purchase_on] [date] NULL,
	[last_mailing_on] [date] NULL,
	[is_deleted] [bit] NOT NULL,
	[deleted_at] [datetimeoffset](7) NULL,
	[deleted_by] [bigint] NULL,
	[created_at] [datetimeoffset](7) NOT NULL,
	[created_by] [bigint] NULL,
	[modified_at] [datetimeoffset](7) NULL,
	[modified_by] [bigint] NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_customers] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

CREATE UNIQUE NONCLUSTERED INDEX [ix_customers_location_id_customer_number] ON [dbo].[customers]
(
	[location_id] ASC,
	[customer_number] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[data_protection_keys](
	[id] [int] IDENTITY(1,1) NOT NULL,
	[friendly_name] [nvarchar](max) NULL,
	[xml] [nvarchar](max) NULL,
 CONSTRAINT [pk_data_protection_keys] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[departments](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[location_id] [bigint] NOT NULL,
	[name] [nvarchar](100) NOT NULL,
	[code] [nvarchar](20) NULL,
	[sort_order] [int] NOT NULL,
	[is_active] [bit] NOT NULL,
	[is_deleted] [bit] NOT NULL,
	[deleted_at] [datetimeoffset](7) NULL,
	[deleted_by] [bigint] NULL,
	[created_at] [datetimeoffset](7) NOT NULL,
	[created_by] [bigint] NULL,
	[modified_at] [datetimeoffset](7) NULL,
	[modified_by] [bigint] NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_departments] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

SET ANSI_PADDING ON

CREATE UNIQUE NONCLUSTERED INDEX [ix_departments_location_id_name] ON [dbo].[departments]
(
	[location_id] ASC,
	[name] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[device_enrolments](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[device_id] [bigint] NOT NULL,
	[location_id] [bigint] NOT NULL,
	[token_hash] [nvarchar](64) NOT NULL,
	[expires_at] [datetimeoffset](7) NOT NULL,
	[redeemed_at] [datetimeoffset](7) NULL,
	[redeemed_by_hostname] [nvarchar](200) NULL,
	[created_at] [datetimeoffset](7) NOT NULL,
	[created_by] [bigint] NULL,
	[modified_at] [datetimeoffset](7) NULL,
	[modified_by] [bigint] NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_device_enrolments] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

CREATE NONCLUSTERED INDEX [ix_device_enrolments_device_id] ON [dbo].[device_enrolments]
(
	[device_id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
SET ANSI_PADDING ON

CREATE UNIQUE NONCLUSTERED INDEX [ix_device_enrolments_token_hash] ON [dbo].[device_enrolments]
(
	[token_hash] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
ALTER TABLE [dbo].[device_enrolments]  WITH CHECK ADD  CONSTRAINT [fk_device_enrolments_devices_device_id] FOREIGN KEY([device_id])
REFERENCES [dbo].[devices] ([id])
ON DELETE CASCADE
ALTER TABLE [dbo].[device_enrolments] CHECK CONSTRAINT [fk_device_enrolments_devices_device_id]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[devices](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[device_key] [nvarchar](40) NOT NULL,
	[name] [nvarchar](120) NULL,
	[location_id] [bigint] NOT NULL,
	[hostname] [nvarchar](200) NULL,
	[local_ip_addresses] [nvarchar](400) NULL,
	[operating_system] [nvarchar](200) NULL,
	[agent_version] [nvarchar](40) NULL,
	[last_heartbeat] [datetimeoffset](7) NULL,
	[is_enabled] [bit] NOT NULL,
	[created_at] [datetimeoffset](7) NOT NULL,
	[created_by] [bigint] NULL,
	[modified_at] [datetimeoffset](7) NULL,
	[modified_by] [bigint] NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_devices] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

SET ANSI_PADDING ON

CREATE UNIQUE NONCLUSTERED INDEX [ix_devices_location_id_device_key] ON [dbo].[devices]
(
	[location_id] ASC,
	[device_key] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[drawer_ledger_entries](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[drawer_session_id] [bigint] NOT NULL,
	[entry_type] [nvarchar](20) NOT NULL,
	[amount] [decimal](19, 4) NOT NULL,
	[reason] [nvarchar](200) NULL,
	[transaction_id] [bigint] NULL,
	[staff_id] [bigint] NOT NULL,
	[occurred_at] [datetimeoffset](7) NOT NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_drawer_ledger_entries] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

CREATE NONCLUSTERED INDEX [ix_drawer_ledger_entries_drawer_session_id] ON [dbo].[drawer_ledger_entries]
(
	[drawer_session_id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[drawer_sessions](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[station_id] [bigint] NOT NULL,
	[location_id] [bigint] NOT NULL,
	[opened_by_staff_id] [bigint] NOT NULL,
	[closed_by_staff_id] [bigint] NULL,
	[opening_float] [decimal](19, 4) NOT NULL,
	[status] [nvarchar](20) NOT NULL,
	[business_date] [date] NOT NULL,
	[opened_at] [datetimeoffset](7) NOT NULL,
	[closed_at] [datetimeoffset](7) NULL,
	[counted_cash] [decimal](19, 4) NULL,
	[expected_cash] [decimal](19, 4) NOT NULL,
	[variance] [decimal](19, 4) NOT NULL,
	[tender_totals_json] [nvarchar](max) NULL,
	[department_net_sales_json] [nvarchar](max) NULL,
	[net_sales] [decimal](19, 4) NOT NULL,
	[tax1collected] [decimal](19, 4) NOT NULL,
	[tax2collected] [decimal](19, 4) NOT NULL,
	[cost_of_goods_sold] [decimal](19, 4) NOT NULL,
	[transaction_count] [int] NOT NULL,
	[created_at] [datetimeoffset](7) NOT NULL,
	[created_by] [bigint] NULL,
	[modified_at] [datetimeoffset](7) NULL,
	[modified_by] [bigint] NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_drawer_sessions] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

SET ANSI_PADDING ON

CREATE NONCLUSTERED INDEX [ix_drawer_sessions_station_id_status] ON [dbo].[drawer_sessions]
(
	[station_id] ASC,
	[status] ASC
)
WHERE ([status]='Open')
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[external_entity_maps](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[provider] [nvarchar](max) NOT NULL,
	[entity_type] [nvarchar](max) NOT NULL,
	[local_id] [bigint] NULL,
	[local_key] [nvarchar](max) NULL,
	[remote_id] [nvarchar](max) NOT NULL,
	[remote_name] [nvarchar](max) NULL,
	[last_synced_at] [datetimeoffset](7) NULL,
	[content_hash] [nvarchar](max) NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_external_entity_maps] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[fiscal_years](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[location_id] [bigint] NOT NULL,
	[year] [int] NOT NULL,
	[starts_on] [date] NOT NULL,
	[ends_on] [date] NOT NULL,
	[status] [nvarchar](20) NOT NULL,
	[closed_at] [datetimeoffset](7) NULL,
	[closed_by] [bigint] NULL,
	[archived_rows] [int] NOT NULL,
	[archived_net_sales] [decimal](19, 2) NOT NULL,
	[notes] [nvarchar](500) NULL,
	[created_at] [datetimeoffset](7) NOT NULL,
	[created_by] [bigint] NULL,
	[modified_at] [datetimeoffset](7) NULL,
	[modified_by] [bigint] NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_fiscal_years] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

CREATE NONCLUSTERED INDEX [ix_fiscal_years_location_id_starts_on] ON [dbo].[fiscal_years]
(
	[location_id] ASC,
	[starts_on] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
CREATE UNIQUE NONCLUSTERED INDEX [ix_fiscal_years_location_id_year] ON [dbo].[fiscal_years]
(
	[location_id] ASC,
	[year] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[gift_cards](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[serial_number] [nvarchar](max) NOT NULL,
	[original_value] [decimal](19, 4) NOT NULL,
	[remaining_value] [decimal](19, 4) NOT NULL,
	[issued_to_customer_id] [bigint] NULL,
	[issued_on] [date] NOT NULL,
	[expires_on] [date] NULL,
	[is_active] [bit] NOT NULL,
	[created_at] [datetimeoffset](7) NOT NULL,
	[created_by] [bigint] NULL,
	[modified_at] [datetimeoffset](7) NULL,
	[modified_by] [bigint] NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_gift_cards] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[gift_certificates](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[serial_number] [nvarchar](max) NOT NULL,
	[original_value] [decimal](19, 4) NOT NULL,
	[remaining_value] [decimal](19, 4) NOT NULL,
	[issued_to_customer_id] [bigint] NULL,
	[issued_on] [date] NOT NULL,
	[expires_on] [date] NULL,
	[is_active] [bit] NOT NULL,
	[created_at] [datetimeoffset](7) NOT NULL,
	[created_by] [bigint] NULL,
	[modified_at] [datetimeoffset](7) NULL,
	[modified_by] [bigint] NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_gift_certificates] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[invoice_payments](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[invoice_id] [bigint] NOT NULL,
	[amount] [decimal](19, 4) NOT NULL,
	[applied_to_penalty] [decimal](19, 4) NOT NULL,
	[applied_to_principal] [decimal](19, 4) NOT NULL,
	[tender_type_id] [bigint] NOT NULL,
	[paid_on] [date] NOT NULL,
	[was_distributed] [bit] NOT NULL,
	[created_at] [datetimeoffset](7) NOT NULL,
	[created_by] [bigint] NULL,
	[modified_at] [datetimeoffset](7) NULL,
	[modified_by] [bigint] NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_invoice_payments] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[invoices](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[invoice_number] [bigint] NOT NULL,
	[customer_id] [bigint] NOT NULL,
	[transaction_id] [bigint] NOT NULL,
	[issued_on] [date] NOT NULL,
	[due_on] [date] NOT NULL,
	[invoice_total] [decimal](19, 4) NOT NULL,
	[penalty_accrued] [decimal](19, 4) NOT NULL,
	[balance_due] [decimal](19, 4) NOT NULL,
	[last_payment_on] [date] NULL,
	[status] [nvarchar](20) NOT NULL,
	[staff_id] [bigint] NOT NULL,
	[created_at] [datetimeoffset](7) NOT NULL,
	[created_by] [bigint] NULL,
	[modified_at] [datetimeoffset](7) NULL,
	[modified_by] [bigint] NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_invoices] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

SET ANSI_PADDING ON

CREATE NONCLUSTERED INDEX [ix_invoices_customer_id_status] ON [dbo].[invoices]
(
	[customer_id] ASC,
	[status] ASC
)
WHERE ([status]='Open')
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
CREATE NONCLUSTERED INDEX [ix_invoices_invoice_number] ON [dbo].[invoices]
(
	[invoice_number] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[kit_components](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[kit_product_id] [bigint] NOT NULL,
	[component_product_id] [bigint] NOT NULL,
	[quantity] [decimal](19, 4) NOT NULL,
	[reduce_stock] [bit] NOT NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_kit_components] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[late_charge_policies](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[location_id] [bigint] NOT NULL,
	[monthly_rate] [decimal](19, 4) NOT NULL,
	[grace_period_days] [int] NOT NULL,
	[is_enabled] [bit] NOT NULL,
	[created_at] [datetimeoffset](7) NOT NULL,
	[created_by] [bigint] NULL,
	[modified_at] [datetimeoffset](7) NULL,
	[modified_by] [bigint] NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_late_charge_policies] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[layaway_lines](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[layaway_id] [bigint] NOT NULL,
	[product_id] [bigint] NOT NULL,
	[variant_id] [bigint] NULL,
	[quantity] [decimal](19, 4) NOT NULL,
	[unit_price] [decimal](19, 4) NOT NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_layaway_lines] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[layaway_payments](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[layaway_id] [bigint] NOT NULL,
	[amount] [decimal](19, 4) NOT NULL,
	[tender_type_id] [bigint] NOT NULL,
	[paid_on] [date] NOT NULL,
	[created_at] [datetimeoffset](7) NOT NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_layaway_payments] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[layaways](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[layaway_number] [bigint] NOT NULL,
	[customer_id] [bigint] NOT NULL,
	[location_id] [bigint] NOT NULL,
	[status] [int] NOT NULL,
	[total] [decimal](19, 4) NOT NULL,
	[amount_paid] [decimal](19, 4) NOT NULL,
	[created_on] [date] NOT NULL,
	[staff_id] [bigint] NOT NULL,
	[created_at] [datetimeoffset](7) NOT NULL,
	[created_by] [bigint] NULL,
	[modified_at] [datetimeoffset](7) NULL,
	[modified_by] [bigint] NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_layaways] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[locations](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[name] [nvarchar](100) NOT NULL,
	[legacy_code] [nvarchar](3) NOT NULL,
	[address_line1] [nvarchar](200) NULL,
	[address_line2] [nvarchar](200) NULL,
	[address_city] [nvarchar](100) NULL,
	[address_state_or_province] [nvarchar](100) NULL,
	[address_postal_code] [nvarchar](20) NULL,
	[address_country] [nvarchar](100) NULL,
	[contact_phone] [nvarchar](30) NULL,
	[contact_extension] [nvarchar](10) NULL,
	[contact_mobile] [nvarchar](30) NULL,
	[contact_fax] [nvarchar](30) NULL,
	[contact_email] [nvarchar](200) NULL,
	[contact_website] [nvarchar](200) NULL,
	[time_zone_id] [nvarchar](50) NOT NULL,
	[business_day_start] [time](7) NOT NULL,
	[base_currency_code] [nvarchar](3) NOT NULL,
	[is_active] [bit] NOT NULL,
	[is_deleted] [bit] NOT NULL,
	[deleted_at] [datetimeoffset](7) NULL,
	[deleted_by] [bigint] NULL,
	[created_at] [datetimeoffset](7) NOT NULL,
	[created_by] [bigint] NULL,
	[modified_at] [datetimeoffset](7) NULL,
	[modified_by] [bigint] NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_locations] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

SET ANSI_PADDING ON

CREATE UNIQUE NONCLUSTERED INDEX [ix_locations_legacy_code] ON [dbo].[locations]
(
	[legacy_code] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[loyalty_ledger_entries](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[customer_id] [bigint] NOT NULL,
	[transaction_id] [bigint] NULL,
	[entry_type] [int] NOT NULL,
	[points] [int] NOT NULL,
	[occurred_at] [datetimeoffset](7) NOT NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_loyalty_ledger_entries] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[loyalty_policies](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[location_id] [bigint] NOT NULL,
	[is_enabled] [bit] NOT NULL,
	[points_per_dollar] [decimal](19, 4) NOT NULL,
	[minimum_required] [int] NOT NULL,
	[percent_enabled] [bit] NOT NULL,
	[reward_percent] [decimal](19, 4) NOT NULL,
	[fixed_enabled] [bit] NOT NULL,
	[reward_fixed_amount] [decimal](19, 4) NOT NULL,
	[suppress_if_subtotal_discount_applied] [bit] NOT NULL,
	[created_at] [datetimeoffset](7) NOT NULL,
	[created_by] [bigint] NULL,
	[modified_at] [datetimeoffset](7) NULL,
	[modified_by] [bigint] NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_loyalty_policies] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[matrix_dimensions](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[product_id] [bigint] NOT NULL,
	[position] [int] NOT NULL,
	[name] [nvarchar](max) NOT NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_matrix_dimensions] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[migration_batches](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[location_id] [bigint] NOT NULL,
	[source_file_name] [nvarchar](260) NOT NULL,
	[entity] [nvarchar](30) NOT NULL,
	[source_hash] [nvarchar](64) NOT NULL,
	[stage] [nvarchar](20) NOT NULL,
	[rows_staged] [int] NOT NULL,
	[rows_deleted_in_source] [int] NOT NULL,
	[blocking_errors] [int] NOT NULL,
	[warnings] [int] NOT NULL,
	[rows_imported] [int] NOT NULL,
	[rows_skipped] [int] NOT NULL,
	[analysis_json] [nvarchar](max) NULL,
	[validation_json] [nvarchar](max) NULL,
	[reconciliation_json] [nvarchar](max) NULL,
	[validated_at] [datetimeoffset](7) NULL,
	[dry_run_at] [datetimeoffset](7) NULL,
	[imported_at] [datetimeoffset](7) NULL,
	[notes] [nvarchar](1000) NULL,
	[created_at] [datetimeoffset](7) NOT NULL,
	[created_by] [bigint] NULL,
	[modified_at] [datetimeoffset](7) NULL,
	[modified_by] [bigint] NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_migration_batches] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

SET ANSI_PADDING ON

CREATE NONCLUSTERED INDEX [ix_migration_batches_location_id_stage] ON [dbo].[migration_batches]
(
	[location_id] ASC,
	[stage] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
SET ANSI_PADDING ON

CREATE NONCLUSTERED INDEX [ix_migration_batches_source_hash] ON [dbo].[migration_batches]
(
	[source_hash] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[migration_staging_rows](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[batch_id] [bigint] NOT NULL,
	[row_number] [int] NOT NULL,
	[payload_json] [nvarchar](max) NOT NULL,
	[is_deleted_in_source] [bit] NOT NULL,
	[legacy_key] [nvarchar](60) NULL,
	[is_valid] [bit] NULL,
	[problems] [nvarchar](2000) NULL,
	[outcome] [nvarchar](200) NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_migration_staging_rows] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

SET ANSI_PADDING ON

CREATE NONCLUSTERED INDEX [ix_migration_staging_rows_batch_id_legacy_key] ON [dbo].[migration_staging_rows]
(
	[batch_id] ASC,
	[legacy_key] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
CREATE NONCLUSTERED INDEX [ix_migration_staging_rows_batch_id_row_number] ON [dbo].[migration_staging_rows]
(
	[batch_id] ASC,
	[row_number] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[number_sequences](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[location_id] [bigint] NOT NULL,
	[kind] [nvarchar](30) NOT NULL,
	[prefix] [nvarchar](10) NOT NULL,
	[pad_width] [int] NOT NULL,
	[next_number] [bigint] NOT NULL,
	[high_water_mark] [bigint] NOT NULL,
	[created_at] [datetimeoffset](7) NOT NULL,
	[created_by] [bigint] NULL,
	[modified_at] [datetimeoffset](7) NULL,
	[modified_by] [bigint] NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_number_sequences] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

SET ANSI_PADDING ON

CREATE UNIQUE NONCLUSTERED INDEX [ix_number_sequences_location_id_kind] ON [dbo].[number_sequences]
(
	[location_id] ASC,
	[kind] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[OpenIddictApplications](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[application_type] [nvarchar](50) NULL,
	[client_id] [nvarchar](100) NULL,
	[client_secret] [nvarchar](max) NULL,
	[client_type] [nvarchar](50) NULL,
	[concurrency_token] [nvarchar](50) NULL,
	[consent_type] [nvarchar](50) NULL,
	[display_name] [nvarchar](max) NULL,
	[display_names] [nvarchar](max) NULL,
	[json_web_key_set] [nvarchar](max) NULL,
	[permissions] [nvarchar](max) NULL,
	[post_logout_redirect_uris] [nvarchar](max) NULL,
	[properties] [nvarchar](max) NULL,
	[redirect_uris] [nvarchar](max) NULL,
	[requirements] [nvarchar](max) NULL,
	[settings] [nvarchar](max) NULL,
 CONSTRAINT [pk_open_iddict_applications] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

SET ANSI_PADDING ON

CREATE UNIQUE NONCLUSTERED INDEX [ix_open_iddict_applications_client_id] ON [dbo].[OpenIddictApplications]
(
	[client_id] ASC
)
WHERE ([client_id] IS NOT NULL)
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[OpenIddictAuthorizations](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[application_id] [bigint] NULL,
	[concurrency_token] [nvarchar](50) NULL,
	[creation_date] [datetime2](7) NULL,
	[properties] [nvarchar](max) NULL,
	[scopes] [nvarchar](max) NULL,
	[status] [nvarchar](50) NULL,
	[subject] [nvarchar](400) NULL,
	[type] [nvarchar](50) NULL,
 CONSTRAINT [pk_open_iddict_authorizations] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

SET ANSI_PADDING ON

CREATE NONCLUSTERED INDEX [ix_open_iddict_authorizations_application_id_status_subject_type] ON [dbo].[OpenIddictAuthorizations]
(
	[application_id] ASC,
	[status] ASC,
	[subject] ASC,
	[type] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
ALTER TABLE [dbo].[OpenIddictAuthorizations]  WITH CHECK ADD  CONSTRAINT [fk_open_iddict_authorizations_open_iddict_applications_application_id] FOREIGN KEY([application_id])
REFERENCES [dbo].[OpenIddictApplications] ([id])
ALTER TABLE [dbo].[OpenIddictAuthorizations] CHECK CONSTRAINT [fk_open_iddict_authorizations_open_iddict_applications_application_id]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[OpenIddictScopes](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[concurrency_token] [nvarchar](50) NULL,
	[description] [nvarchar](max) NULL,
	[descriptions] [nvarchar](max) NULL,
	[display_name] [nvarchar](max) NULL,
	[display_names] [nvarchar](max) NULL,
	[name] [nvarchar](200) NULL,
	[properties] [nvarchar](max) NULL,
	[resources] [nvarchar](max) NULL,
 CONSTRAINT [pk_open_iddict_scopes] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

SET ANSI_PADDING ON

CREATE UNIQUE NONCLUSTERED INDEX [ix_open_iddict_scopes_name] ON [dbo].[OpenIddictScopes]
(
	[name] ASC
)
WHERE ([name] IS NOT NULL)
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[OpenIddictTokens](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[application_id] [bigint] NULL,
	[authorization_id] [bigint] NULL,
	[concurrency_token] [nvarchar](50) NULL,
	[creation_date] [datetime2](7) NULL,
	[expiration_date] [datetime2](7) NULL,
	[payload] [nvarchar](max) NULL,
	[properties] [nvarchar](max) NULL,
	[redemption_date] [datetime2](7) NULL,
	[reference_id] [nvarchar](100) NULL,
	[status] [nvarchar](50) NULL,
	[subject] [nvarchar](400) NULL,
	[type] [nvarchar](150) NULL,
 CONSTRAINT [pk_open_iddict_tokens] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

SET ANSI_PADDING ON

CREATE NONCLUSTERED INDEX [ix_open_iddict_tokens_application_id_status_subject_type] ON [dbo].[OpenIddictTokens]
(
	[application_id] ASC,
	[status] ASC,
	[subject] ASC,
	[type] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
CREATE NONCLUSTERED INDEX [ix_open_iddict_tokens_authorization_id] ON [dbo].[OpenIddictTokens]
(
	[authorization_id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
SET ANSI_PADDING ON

CREATE UNIQUE NONCLUSTERED INDEX [ix_open_iddict_tokens_reference_id] ON [dbo].[OpenIddictTokens]
(
	[reference_id] ASC
)
WHERE ([reference_id] IS NOT NULL)
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
ALTER TABLE [dbo].[OpenIddictTokens]  WITH CHECK ADD  CONSTRAINT [fk_open_iddict_tokens_open_iddict_applications_application_id] FOREIGN KEY([application_id])
REFERENCES [dbo].[OpenIddictApplications] ([id])
ALTER TABLE [dbo].[OpenIddictTokens] CHECK CONSTRAINT [fk_open_iddict_tokens_open_iddict_applications_application_id]
ALTER TABLE [dbo].[OpenIddictTokens]  WITH CHECK ADD  CONSTRAINT [fk_open_iddict_tokens_open_iddict_authorizations_authorization_id] FOREIGN KEY([authorization_id])
REFERENCES [dbo].[OpenIddictAuthorizations] ([id])
ALTER TABLE [dbo].[OpenIddictTokens] CHECK CONSTRAINT [fk_open_iddict_tokens_open_iddict_authorizations_authorization_id]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[permissions](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[key] [nvarchar](60) NOT NULL,
	[description] [nvarchar](200) NOT NULL,
	[group] [nvarchar](40) NOT NULL,
	[is_sensitive] [bit] NOT NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_permissions] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

SET ANSI_PADDING ON

CREATE UNIQUE NONCLUSTERED INDEX [ix_permissions_key] ON [dbo].[permissions]
(
	[key] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[pole_display_profiles](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[location_id] [bigint] NOT NULL,
	[station_id] [bigint] NULL,
	[name] [nvarchar](60) NOT NULL,
	[port] [nvarchar](60) NOT NULL,
	[baud_rate] [int] NOT NULL,
	[line1width] [int] NOT NULL,
	[line2width] [int] NOT NULL,
	[idle_line1] [nvarchar](60) NOT NULL,
	[idle_line2] [nvarchar](60) NOT NULL,
	[clear_command] [nvarchar](60) NOT NULL,
	[line1command] [nvarchar](60) NOT NULL,
	[line2command] [nvarchar](60) NOT NULL,
	[is_active] [bit] NOT NULL,
	[created_at] [datetimeoffset](7) NOT NULL,
	[created_by] [bigint] NULL,
	[modified_at] [datetimeoffset](7) NULL,
	[modified_by] [bigint] NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_pole_display_profiles] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

CREATE NONCLUSTERED INDEX [ix_pole_display_profiles_location_id_station_id] ON [dbo].[pole_display_profiles]
(
	[location_id] ASC,
	[station_id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[pos_policies](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[location_id] [bigint] NOT NULL,
	[apply_tax1] [bit] NOT NULL,
	[apply_tax2] [bit] NOT NULL,
	[allow_tax_override] [bit] NOT NULL,
	[apply_add_on_charge] [bit] NOT NULL,
	[fast_scan_mode] [bit] NOT NULL,
	[auto_save_sales] [bit] NOT NULL,
	[confirm_before_saving_sales] [bit] NOT NULL,
	[scan_random_weight_barcodes] [bit] NOT NULL,
	[staff_may_discount] [bit] NOT NULL,
	[allow_item_list_edit] [bit] NOT NULL,
	[track_staff_sales] [bit] NOT NULL,
	[require_supervisor_to_void] [bit] NOT NULL,
	[use_employee_time_clock] [bit] NOT NULL,
	[print_credit_card_signature_line] [bit] NOT NULL,
	[print_client_name_on_sales_slip] [bit] NOT NULL,
	[carry_over_city_state_zip] [bit] NOT NULL,
	[default_tender_type_id] [bigint] NULL,
	[abandoned_cart_timeout_minutes] [int] NOT NULL,
	[created_at] [datetimeoffset](7) NOT NULL,
	[created_by] [bigint] NULL,
	[modified_at] [datetimeoffset](7) NULL,
	[modified_by] [bigint] NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_pos_policies] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[price_breaks](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[product_id] [bigint] NOT NULL,
	[level] [int] NOT NULL,
	[min_quantity] [decimal](19, 4) NOT NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_price_breaks] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[price_quote_lines](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[price_quote_id] [bigint] NOT NULL,
	[product_id] [bigint] NOT NULL,
	[variant_id] [bigint] NULL,
	[quantity] [decimal](19, 4) NOT NULL,
	[unit_price] [decimal](19, 4) NOT NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_price_quote_lines] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[price_quotes](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[quote_number] [bigint] NOT NULL,
	[customer_id] [bigint] NOT NULL,
	[location_id] [bigint] NOT NULL,
	[status] [int] NOT NULL,
	[issued_on] [date] NOT NULL,
	[expires_on] [date] NULL,
	[total] [decimal](19, 4) NOT NULL,
	[staff_id] [bigint] NOT NULL,
	[created_at] [datetimeoffset](7) NOT NULL,
	[created_by] [bigint] NULL,
	[modified_at] [datetimeoffset](7) NULL,
	[modified_by] [bigint] NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_price_quotes] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[pricing_rule_settings](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[location_id] [bigint] NOT NULL,
	[rule_key] [nvarchar](40) NOT NULL,
	[order] [int] NOT NULL,
	[enabled] [bit] NOT NULL,
	[parameters_json] [nvarchar](max) NULL,
	[created_at] [datetimeoffset](7) NOT NULL,
	[created_by] [bigint] NULL,
	[modified_at] [datetimeoffset](7) NULL,
	[modified_by] [bigint] NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_pricing_rule_settings] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

SET ANSI_PADDING ON

CREATE UNIQUE NONCLUSTERED INDEX [ix_pricing_rule_settings_location_id_rule_key] ON [dbo].[pricing_rule_settings]
(
	[location_id] ASC,
	[rule_key] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[printer_profiles](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[location_id] [bigint] NOT NULL,
	[station_id] [bigint] NULL,
	[name] [nvarchar](60) NOT NULL,
	[setup_command] [nvarchar](120) NULL,
	[cutter_command] [nvarchar](120) NULL,
	[red_command] [nvarchar](120) NULL,
	[black_command] [nvarchar](120) NULL,
	[port] [nvarchar](120) NULL,
	[default_copies] [int] NOT NULL,
	[page_eject] [bit] NOT NULL,
	[extra_copy_on_card] [bit] NOT NULL,
	[initialize_serial] [bit] NOT NULL,
	[output] [nvarchar](16) NOT NULL,
	[columns] [int] NOT NULL,
	[drawer_trigger] [nvarchar](120) NOT NULL,
	[drawer_repeat] [int] NOT NULL,
	[open_drawer_on_print] [bit] NOT NULL,
	[is_active] [bit] NOT NULL,
	[created_at] [datetimeoffset](7) NOT NULL,
	[created_by] [bigint] NULL,
	[modified_at] [datetimeoffset](7) NULL,
	[modified_by] [bigint] NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_printer_profiles] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

CREATE NONCLUSTERED INDEX [ix_printer_profiles_location_id_station_id] ON [dbo].[printer_profiles]
(
	[location_id] ASC,
	[station_id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[product_images](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[product_id] [bigint] NOT NULL,
	[content] [varbinary](max) NOT NULL,
	[content_type] [nvarchar](40) NOT NULL,
	[e_tag] [nvarchar](32) NOT NULL,
	[created_at] [datetimeoffset](7) NOT NULL,
	[created_by] [bigint] NULL,
	[modified_at] [datetimeoffset](7) NULL,
	[modified_by] [bigint] NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_product_images] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

CREATE UNIQUE NONCLUSTERED INDEX [ix_product_images_product_id] ON [dbo].[product_images]
(
	[product_id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
ALTER TABLE [dbo].[product_images]  WITH CHECK ADD  CONSTRAINT [fk_product_images_products_product_id] FOREIGN KEY([product_id])
REFERENCES [dbo].[products] ([id])
ON DELETE CASCADE
ALTER TABLE [dbo].[product_images] CHECK CONSTRAINT [fk_product_images_products_product_id]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[product_prices](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[product_id] [bigint] NOT NULL,
	[level] [int] NOT NULL,
	[price] [decimal](19, 4) NOT NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_product_prices] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[product_suppliers](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[product_id] [bigint] NOT NULL,
	[supplier_id] [bigint] NOT NULL,
	[rank] [int] NOT NULL,
	[cost] [decimal](19, 4) NOT NULL,
	[reorder_number] [nvarchar](max) NULL,
	[case_qty] [decimal](19, 4) NOT NULL,
	[minimum_order_qty] [decimal](19, 4) NOT NULL,
	[created_at] [datetimeoffset](7) NOT NULL,
	[created_by] [bigint] NULL,
	[modified_at] [datetimeoffset](7) NULL,
	[modified_by] [bigint] NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_product_suppliers] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[product_variants](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[product_id] [bigint] NOT NULL,
	[dim1value] [nvarchar](max) NOT NULL,
	[dim2value] [nvarchar](max) NULL,
	[dim3value] [nvarchar](max) NULL,
	[variant_code] [nvarchar](max) NOT NULL,
	[upc] [nvarchar](max) NULL,
	[on_hand] [decimal](19, 4) NOT NULL,
	[is_active] [bit] NOT NULL,
	[created_at] [datetimeoffset](7) NOT NULL,
	[created_by] [bigint] NULL,
	[modified_at] [datetimeoffset](7) NULL,
	[modified_by] [bigint] NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_product_variants] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[products](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[location_id] [bigint] NOT NULL,
	[stock_code] [nvarchar](30) NOT NULL,
	[name] [nvarchar](200) NOT NULL,
	[description] [nvarchar](max) NULL,
	[type] [int] NOT NULL,
	[upc] [nvarchar](30) NULL,
	[tax1applies] [bit] NOT NULL,
	[tax2applies] [bit] NOT NULL,
	[regular_price] [decimal](19, 4) NOT NULL,
	[last_cost] [decimal](19, 4) NOT NULL,
	[avg_cost] [decimal](19, 4) NOT NULL,
	[gross_margin_pct] [decimal](19, 4) NOT NULL,
	[base_stock] [int] NOT NULL,
	[reorder_point] [int] NOT NULL,
	[reorder_qty] [int] NOT NULL,
	[on_hand] [decimal](19, 4) NOT NULL,
	[on_order] [decimal](19, 4) NOT NULL,
	[case_qty] [decimal](19, 4) NOT NULL,
	[ship_weight] [decimal](19, 4) NOT NULL,
	[bin_location] [nvarchar](50) NULL,
	[pos_message] [nvarchar](max) NULL,
	[invoice_message] [nvarchar](max) NULL,
	[notes] [nvarchar](max) NULL,
	[has_image] [bit] NOT NULL,
	[department_id] [bigint] NULL,
	[category_id] [bigint] NULL,
	[substitute_product_id] [bigint] NULL,
	[tag_along_product_id] [bigint] NULL,
	[parent_product_id] [bigint] NULL,
	[is_deleted] [bit] NOT NULL,
	[deleted_at] [datetimeoffset](7) NULL,
	[deleted_by] [bigint] NULL,
	[created_at] [datetimeoffset](7) NOT NULL,
	[created_by] [bigint] NULL,
	[modified_at] [datetimeoffset](7) NULL,
	[modified_by] [bigint] NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_products] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

SET ANSI_PADDING ON

CREATE UNIQUE NONCLUSTERED INDEX [ix_products_location_id_stock_code] ON [dbo].[products]
(
	[location_id] ASC,
	[stock_code] ASC
)
WHERE ([is_deleted]=(0))
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
SET ANSI_PADDING ON

CREATE UNIQUE NONCLUSTERED INDEX [ix_products_location_id_upc] ON [dbo].[products]
(
	[location_id] ASC,
	[upc] ASC
)
WHERE ([upc] IS NOT NULL AND [upc]<>'' AND [is_deleted]=(0))
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[purchase_order_lines](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[purchase_order_id] [bigint] NOT NULL,
	[product_id] [bigint] NOT NULL,
	[variant_id] [bigint] NULL,
	[order_qty] [decimal](19, 4) NOT NULL,
	[case_qty] [decimal](19, 4) NOT NULL,
	[cost_each] [decimal](19, 4) NOT NULL,
	[order_cost] [decimal](19, 4) NOT NULL,
	[qty_received] [decimal](19, 4) NOT NULL,
	[in_stock_at_generation] [decimal](19, 4) NOT NULL,
	[on_order_at_generation] [decimal](19, 4) NOT NULL,
	[back_orders] [decimal](19, 4) NOT NULL,
	[created_at] [datetimeoffset](7) NOT NULL,
	[created_by] [bigint] NULL,
	[modified_at] [datetimeoffset](7) NULL,
	[modified_by] [bigint] NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_purchase_order_lines] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[purchase_order_receipts](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[purchase_order_id] [bigint] NOT NULL,
	[received_on] [date] NOT NULL,
	[freight_total] [decimal](19, 4) NOT NULL,
	[staff_id] [bigint] NOT NULL,
	[created_at] [datetimeoffset](7) NOT NULL,
	[created_by] [bigint] NULL,
	[modified_at] [datetimeoffset](7) NULL,
	[modified_by] [bigint] NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_purchase_order_receipts] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[purchase_orders](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[po_number] [bigint] NOT NULL,
	[supplier_id] [bigint] NOT NULL,
	[location_id] [bigint] NOT NULL,
	[status] [int] NOT NULL,
	[quantity_strategy] [int] NOT NULL,
	[header_text] [nvarchar](max) NULL,
	[posted_on] [date] NULL,
	[due_on] [date] NULL,
	[total] [decimal](19, 4) NOT NULL,
	[accounting_bill_ref] [nvarchar](max) NULL,
	[created_at] [datetimeoffset](7) NOT NULL,
	[created_by] [bigint] NULL,
	[modified_at] [datetimeoffset](7) NULL,
	[modified_by] [bigint] NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_purchase_orders] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[reader_antenna_assignments](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[reader_id] [bigint] NOT NULL,
	[antenna_number] [int] NOT NULL,
	[station_id] [bigint] NOT NULL,
	[is_enabled] [bit] NOT NULL,
	[created_at] [datetimeoffset](7) NOT NULL,
	[created_by] [bigint] NULL,
	[modified_at] [datetimeoffset](7) NULL,
	[modified_by] [bigint] NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_reader_antenna_assignments] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

CREATE NONCLUSTERED INDEX [ix_reader_antenna_assignments_reader_id] ON [dbo].[reader_antenna_assignments]
(
	[reader_id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
CREATE UNIQUE NONCLUSTERED INDEX [ix_reader_antenna_assignments_reader_id_antenna_number] ON [dbo].[reader_antenna_assignments]
(
	[reader_id] ASC,
	[antenna_number] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
CREATE NONCLUSTERED INDEX [ix_reader_antenna_assignments_station_id] ON [dbo].[reader_antenna_assignments]
(
	[station_id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
ALTER TABLE [dbo].[reader_antenna_assignments]  WITH CHECK ADD  CONSTRAINT [fk_reader_antenna_assignments_rfid_readers_reader_id] FOREIGN KEY([reader_id])
REFERENCES [dbo].[rfid_readers] ([id])
ON DELETE CASCADE
ALTER TABLE [dbo].[reader_antenna_assignments] CHECK CONSTRAINT [fk_reader_antenna_assignments_rfid_readers_reader_id]
ALTER TABLE [dbo].[reader_antenna_assignments]  WITH CHECK ADD  CONSTRAINT [fk_reader_antenna_assignments_stations_station_id] FOREIGN KEY([station_id])
REFERENCES [dbo].[stations] ([id])
ALTER TABLE [dbo].[reader_antenna_assignments] CHECK CONSTRAINT [fk_reader_antenna_assignments_stations_station_id]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[reader_profiles](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[location_id] [bigint] NOT NULL,
	[station_id] [bigint] NULL,
	[name] [nvarchar](60) NOT NULL,
	[host] [nvarchar](120) NOT NULL,
	[port] [int] NOT NULL,
	[protocol] [nvarchar](16) NOT NULL,
	[antenna_zones] [nvarchar](200) NOT NULL,
	[rssi_threshold_dbm] [int] NOT NULL,
	[minimum_read_count] [int] NOT NULL,
	[debounce_ms] [int] NOT NULL,
	[coalesce_ms] [int] NOT NULL,
	[flush_interval_ms] [int] NOT NULL,
	[max_batch_size] [int] NOT NULL,
	[auto_accept_batches] [bit] NOT NULL,
	[continuous_mode] [bit] NOT NULL,
	[output_power_dbm] [nvarchar](max) NOT NULL,
	[region] [int] NOT NULL,
	[frequency_start_index] [int] NOT NULL,
	[frequency_end_index] [int] NOT NULL,
	[link_profile] [int] NOT NULL,
	[beeper] [int] NOT NULL,
	[antenna_return_loss_threshold_db] [int] NOT NULL,
	[impinj_fast_tid] [bit] NOT NULL,
	[dense_reader_mode] [bit] NOT NULL,
	[device_address] [int] NOT NULL,
	[is_active] [bit] NOT NULL,
	[created_at] [datetimeoffset](7) NOT NULL,
	[created_by] [bigint] NULL,
	[modified_at] [datetimeoffset](7) NULL,
	[modified_by] [bigint] NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_reader_profiles] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

CREATE NONCLUSTERED INDEX [ix_reader_profiles_location_id_station_id] ON [dbo].[reader_profiles]
(
	[location_id] ASC,
	[station_id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[rfid_readers](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[reader_key] [nvarchar](40) NOT NULL,
	[serial_number] [nvarchar](80) NULL,
	[model] [nvarchar](120) NULL,
	[location_id] [bigint] NOT NULL,
	[device_id] [bigint] NULL,
	[host] [nvarchar](200) NOT NULL,
	[port] [int] NOT NULL,
	[protocol] [int] NOT NULL,
	[antenna_count] [int] NOT NULL,
	[last_seen] [datetimeoffset](7) NULL,
	[is_enabled] [bit] NOT NULL,
	[created_at] [datetimeoffset](7) NOT NULL,
	[created_by] [bigint] NULL,
	[modified_at] [datetimeoffset](7) NULL,
	[modified_by] [bigint] NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_rfid_readers] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

CREATE NONCLUSTERED INDEX [ix_rfid_readers_device_id] ON [dbo].[rfid_readers]
(
	[device_id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
SET ANSI_PADDING ON

CREATE UNIQUE NONCLUSTERED INDEX [ix_rfid_readers_location_id_reader_key] ON [dbo].[rfid_readers]
(
	[location_id] ASC,
	[reader_key] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
SET ANSI_PADDING ON

CREATE UNIQUE NONCLUSTERED INDEX [ix_rfid_readers_serial_number] ON [dbo].[rfid_readers]
(
	[serial_number] ASC
)
WHERE ([serial_number] IS NOT NULL)
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
ALTER TABLE [dbo].[rfid_readers]  WITH CHECK ADD  CONSTRAINT [fk_rfid_readers_devices_device_id] FOREIGN KEY([device_id])
REFERENCES [dbo].[devices] ([id])
ALTER TABLE [dbo].[rfid_readers] CHECK CONSTRAINT [fk_rfid_readers_devices_device_id]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[role_permissions](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[role_id] [bigint] NOT NULL,
	[permission_key] [nvarchar](60) NOT NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_role_permissions] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

SET ANSI_PADDING ON

CREATE UNIQUE NONCLUSTERED INDEX [ix_role_permissions_role_id_permission_key] ON [dbo].[role_permissions]
(
	[role_id] ASC,
	[permission_key] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[sale_adjustments](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[transaction_id] [bigint] NOT NULL,
	[type] [nvarchar](24) NOT NULL,
	[label] [nvarchar](120) NOT NULL,
	[amount] [decimal](19, 4) NOT NULL,
	[serial] [nvarchar](64) NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_sale_adjustments] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

CREATE NONCLUSTERED INDEX [ix_sale_adjustments_transaction_id] ON [dbo].[sale_adjustments]
(
	[transaction_id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[sale_lines](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[transaction_id] [bigint] NOT NULL,
	[sequence] [int] NOT NULL,
	[product_id] [bigint] NOT NULL,
	[variant_id] [bigint] NULL,
	[serialized_unit_id] [bigint] NULL,
	[epc] [nvarchar](96) NULL,
	[serial_number] [nvarchar](64) NULL,
	[stock_code_snapshot] [nvarchar](24) NULL,
	[name_snapshot] [nvarchar](200) NULL,
	[source] [nvarchar](20) NOT NULL,
	[quantity] [decimal](18, 4) NOT NULL,
	[chargeable_quantity] [decimal](18, 4) NOT NULL,
	[unit_price] [decimal](19, 4) NOT NULL,
	[discount_pct] [decimal](7, 2) NOT NULL,
	[extended_net] [decimal](19, 4) NOT NULL,
	[prorated_adjustment] [decimal](19, 4) NOT NULL,
	[taxable_net] [decimal](19, 4) NOT NULL,
	[tax1applies] [bit] NOT NULL,
	[tax2applies] [bit] NOT NULL,
	[tax1amount] [decimal](19, 4) NOT NULL,
	[tax2amount] [decimal](19, 4) NOT NULL,
	[unit_cost_snapshot] [decimal](19, 3) NOT NULL,
	[price_origin] [nvarchar](20) NOT NULL,
	[line_type] [nvarchar](20) NOT NULL,
	[returned_to_stock] [bit] NOT NULL,
	[note] [nvarchar](500) NULL,
	[row_version] [bigint] NOT NULL,
	[refunds_sale_line_id] [bigint] NULL,
 CONSTRAINT [pk_sale_lines] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

CREATE NONCLUSTERED INDEX [ix_sale_lines_product_id] ON [dbo].[sale_lines]
(
	[product_id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
CREATE NONCLUSTERED INDEX [ix_sale_lines_refunds_sale_line_id] ON [dbo].[sale_lines]
(
	[refunds_sale_line_id] ASC
)
WHERE ([refunds_sale_line_id] IS NOT NULL)
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
CREATE NONCLUSTERED INDEX [ix_sale_lines_transaction_id] ON [dbo].[sale_lines]
(
	[transaction_id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[sale_pricings](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[product_id] [bigint] NOT NULL,
	[discount_pct] [decimal](19, 4) NOT NULL,
	[starts_on] [date] NOT NULL,
	[ends_on] [date] NOT NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_sale_pricings] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[sale_tax_snapshots](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[transaction_id] [bigint] NOT NULL,
	[tax1name] [nvarchar](40) NOT NULL,
	[tax1rate] [decimal](9, 4) NOT NULL,
	[tax2name] [nvarchar](40) NOT NULL,
	[tax2rate] [decimal](9, 4) NOT NULL,
	[tax2compound] [bit] NOT NULL,
	[add_on_name] [nvarchar](40) NOT NULL,
	[add_on_rate] [decimal](9, 4) NOT NULL,
	[add_on_taxable] [bit] NOT NULL,
	[tax_inclusive] [bit] NOT NULL,
	[tax_registration_number] [nvarchar](40) NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_sale_tax_snapshots] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

CREATE UNIQUE NONCLUSTERED INDEX [ix_sale_tax_snapshots_transaction_id] ON [dbo].[sale_tax_snapshots]
(
	[transaction_id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[sale_tenders](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[transaction_id] [bigint] NOT NULL,
	[tender_type_id] [bigint] NOT NULL,
	[behaviour] [nvarchar](24) NOT NULL,
	[amount] [decimal](19, 4) NOT NULL,
	[amount_tendered] [decimal](19, 4) NOT NULL,
	[change_given] [decimal](19, 4) NOT NULL,
	[currency_id] [bigint] NULL,
	[exchange_rate] [decimal](18, 8) NOT NULL,
	[reference] [nvarchar](64) NULL,
	[auth_code] [nvarchar](32) NULL,
	[card_last4] [nvarchar](4) NULL,
	[gateway_reference] [nvarchar](64) NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_sale_tenders] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

CREATE NONCLUSTERED INDEX [ix_sale_tenders_transaction_id] ON [dbo].[sale_tenders]
(
	[transaction_id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[sales_history_archives](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[fiscal_year_id] [bigint] NOT NULL,
	[location_id] [bigint] NOT NULL,
	[year] [int] NOT NULL,
	[month] [int] NOT NULL,
	[product_id] [bigint] NOT NULL,
	[stock_code_snapshot] [nvarchar](30) NOT NULL,
	[name_snapshot] [nvarchar](200) NOT NULL,
	[department_id] [bigint] NULL,
	[quantity_sold] [decimal](18, 4) NOT NULL,
	[net_sales] [decimal](19, 2) NOT NULL,
	[cost_of_goods_sold] [decimal](19, 3) NOT NULL,
	[transaction_count] [int] NOT NULL,
	[archived_at] [datetimeoffset](7) NOT NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_sales_history_archives] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

CREATE NONCLUSTERED INDEX [ix_sales_history_archives_fiscal_year_id] ON [dbo].[sales_history_archives]
(
	[fiscal_year_id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
CREATE UNIQUE NONCLUSTERED INDEX [ix_sales_history_archives_location_id_year_month_product_id] ON [dbo].[sales_history_archives]
(
	[location_id] ASC,
	[year] ASC,
	[month] ASC,
	[product_id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[sales_transactions](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[transaction_number] [bigint] NOT NULL,
	[location_id] [bigint] NOT NULL,
	[station_id] [bigint] NOT NULL,
	[staff_id] [bigint] NOT NULL,
	[customer_id] [bigint] NULL,
	[drawer_session_id] [bigint] NULL,
	[business_date] [date] NOT NULL,
	[subtotal] [decimal](19, 4) NOT NULL,
	[discount_total] [decimal](19, 4) NOT NULL,
	[add_on_charge_total] [decimal](19, 4) NOT NULL,
	[tax1total] [decimal](19, 4) NOT NULL,
	[tax2total] [decimal](19, 4) NOT NULL,
	[grand_total] [decimal](19, 4) NOT NULL,
	[rounding_adjustment] [decimal](19, 4) NOT NULL,
	[change_given] [decimal](19, 4) NOT NULL,
	[cost_of_goods_sold] [decimal](19, 4) NOT NULL,
	[loyalty_points_earned] [int] NOT NULL,
	[loyalty_points_redeemed] [int] NOT NULL,
	[status] [nvarchar](20) NOT NULL,
	[is_training] [bit] NOT NULL,
	[voided_by_transaction_id] [bigint] NULL,
	[reverses_transaction_id] [bigint] NULL,
	[void_reason] [nvarchar](max) NULL,
	[void_approved_by_staff_id] [bigint] NULL,
	[invoice_id] [bigint] NULL,
	[reprint_count] [int] NOT NULL,
	[completed_at] [datetimeoffset](7) NOT NULL,
	[created_at] [datetimeoffset](7) NOT NULL,
	[created_by] [bigint] NULL,
	[modified_at] [datetimeoffset](7) NULL,
	[modified_by] [bigint] NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_sales_transactions] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

CREATE NONCLUSTERED INDEX [ix_sales_transactions_location_id_completed_at] ON [dbo].[sales_transactions]
(
	[location_id] ASC,
	[completed_at] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
CREATE NONCLUSTERED INDEX [ix_sales_transactions_transaction_number] ON [dbo].[sales_transactions]
(
	[transaction_number] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[scale_profiles](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[location_id] [bigint] NOT NULL,
	[station_id] [bigint] NULL,
	[name] [nvarchar](60) NOT NULL,
	[port] [nvarchar](60) NOT NULL,
	[baud_rate] [int] NOT NULL,
	[data_bits] [int] NOT NULL,
	[parity] [nvarchar](16) NOT NULL,
	[stop_bits] [nvarchar](16) NOT NULL,
	[get_weight_command] [nvarchar](16) NOT NULL,
	[zero_command] [nvarchar](16) NOT NULL,
	[unit] [nvarchar](8) NOT NULL,
	[timeout_ms] [int] NOT NULL,
	[is_active] [bit] NOT NULL,
	[created_at] [datetimeoffset](7) NOT NULL,
	[created_by] [bigint] NULL,
	[modified_at] [datetimeoffset](7) NULL,
	[modified_by] [bigint] NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_scale_profiles] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

CREATE NONCLUSTERED INDEX [ix_scale_profiles_location_id_station_id] ON [dbo].[scale_profiles]
(
	[location_id] ASC,
	[station_id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[serialized_units](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[product_id] [bigint] NOT NULL,
	[variant_id] [bigint] NULL,
	[serial_number] [nvarchar](64) NULL,
	[epc] [nvarchar](96) NULL,
	[state] [nvarchar](20) NOT NULL,
	[location_id] [bigint] NOT NULL,
	[received_on] [datetimeoffset](7) NOT NULL,
	[last_seen_at] [datetimeoffset](7) NULL,
	[created_at] [datetimeoffset](7) NOT NULL,
	[created_by] [bigint] NULL,
	[modified_at] [datetimeoffset](7) NULL,
	[modified_by] [bigint] NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_serialized_units] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

SET ANSI_PADDING ON

CREATE UNIQUE NONCLUSTERED INDEX [ix_serialized_units_epc] ON [dbo].[serialized_units]
(
	[epc] ASC
)
WHERE ([epc] IS NOT NULL)
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
SET ANSI_PADDING ON

CREATE NONCLUSTERED INDEX [ix_serialized_units_product_id_state] ON [dbo].[serialized_units]
(
	[product_id] ASC,
	[state] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[shopper_devices](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[shopper_id] [bigint] NOT NULL,
	[device_id] [nvarchar](128) NOT NULL,
	[device_name] [nvarchar](100) NOT NULL,
	[refresh_token_hash] [nvarchar](200) NOT NULL,
	[refresh_token_expires_at] [datetimeoffset](7) NOT NULL,
	[biometric_enabled] [bit] NOT NULL,
	[last_used_at] [datetimeoffset](7) NULL,
	[revoked_at] [datetimeoffset](7) NULL,
	[created_at] [datetimeoffset](7) NOT NULL,
	[created_by] [bigint] NULL,
	[modified_at] [datetimeoffset](7) NULL,
	[modified_by] [bigint] NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_shopper_devices] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

SET ANSI_PADDING ON

CREATE NONCLUSTERED INDEX [ix_shopper_devices_refresh_token_hash] ON [dbo].[shopper_devices]
(
	[refresh_token_hash] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
SET ANSI_PADDING ON

CREATE UNIQUE NONCLUSTERED INDEX [ix_shopper_devices_shopper_id_device_id] ON [dbo].[shopper_devices]
(
	[shopper_id] ASC,
	[device_id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
ALTER TABLE [dbo].[shopper_devices]  WITH CHECK ADD  CONSTRAINT [fk_shopper_devices_shoppers_shopper_id] FOREIGN KEY([shopper_id])
REFERENCES [dbo].[shoppers] ([id])
ON DELETE CASCADE
ALTER TABLE [dbo].[shopper_devices] CHECK CONSTRAINT [fk_shopper_devices_shoppers_shopper_id]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[shoppers](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[first_name] [nvarchar](100) NOT NULL,
	[last_name] [nvarchar](100) NOT NULL,
	[phone] [nvarchar](30) NOT NULL,
	[email] [nvarchar](200) NOT NULL,
	[normalized_email] [nvarchar](200) NOT NULL,
	[password_hash] [nvarchar](400) NOT NULL,
	[email_confirmed] [bit] NOT NULL,
	[is_active] [bit] NOT NULL,
	[customer_id] [bigint] NULL,
	[last_signed_in_at] [datetimeoffset](7) NULL,
	[created_at] [datetimeoffset](7) NOT NULL,
	[created_by] [bigint] NULL,
	[modified_at] [datetimeoffset](7) NULL,
	[modified_by] [bigint] NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_shoppers] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

SET ANSI_PADDING ON

CREATE UNIQUE NONCLUSTERED INDEX [ix_shoppers_normalized_email] ON [dbo].[shoppers]
(
	[normalized_email] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
SET ANSI_PADDING ON

CREATE NONCLUSTERED INDEX [ix_shoppers_phone] ON [dbo].[shoppers]
(
	[phone] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[staff_profiles](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[user_id] [bigint] NOT NULL,
	[staff_code] [nvarchar](8) NOT NULL,
	[first_name] [nvarchar](100) NOT NULL,
	[last_name] [nvarchar](100) NOT NULL,
	[pin_hash] [nvarchar](256) NULL,
	[failed_pin_attempts] [int] NOT NULL,
	[pin_locked_until] [datetimeoffset](7) NULL,
	[access_level] [int] NOT NULL,
	[is_active] [bit] NOT NULL,
	[created_at] [datetimeoffset](7) NOT NULL,
	[created_by] [bigint] NULL,
	[modified_at] [datetimeoffset](7) NULL,
	[modified_by] [bigint] NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_staff_profiles] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

SET ANSI_PADDING ON

CREATE UNIQUE NONCLUSTERED INDEX [ix_staff_profiles_staff_code] ON [dbo].[staff_profiles]
(
	[staff_code] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
CREATE UNIQUE NONCLUSTERED INDEX [ix_staff_profiles_user_id] ON [dbo].[staff_profiles]
(
	[user_id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[stations](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[station_code] [nvarchar](3) NOT NULL,
	[location_id] [bigint] NOT NULL,
	[name] [nvarchar](100) NULL,
	[fast_scan_mode] [bit] NULL,
	[auto_save_sales] [bit] NULL,
	[confirm_before_saving] [bit] NULL,
	[scan_random_weight_barcodes] [bit] NULL,
	[default_tender_type_id] [bigint] NULL,
	[printer_profile_id] [bigint] NULL,
	[reader_profile_id] [bigint] NULL,
	[scale_profile_id] [bigint] NULL,
	[pole_display_profile_id] [bigint] NULL,
	[reader_mode] [nvarchar](16) NOT NULL,
	[agent_version] [nvarchar](32) NULL,
	[last_heartbeat] [datetimeoffset](7) NULL,
	[agent_token_hash] [nvarchar](128) NULL,
	[is_active] [bit] NOT NULL,
	[created_at] [datetimeoffset](7) NOT NULL,
	[created_by] [bigint] NULL,
	[modified_at] [datetimeoffset](7) NULL,
	[modified_by] [bigint] NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_stations] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

SET ANSI_PADDING ON

CREATE UNIQUE NONCLUSTERED INDEX [ix_stations_location_id_station_code] ON [dbo].[stations]
(
	[location_id] ASC,
	[station_code] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[stock_count_lines](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[stock_count_id] [bigint] NOT NULL,
	[product_id] [bigint] NOT NULL,
	[variant_id] [bigint] NULL,
	[stock_code] [nvarchar](30) NOT NULL,
	[product_name] [nvarchar](200) NOT NULL,
	[counted_qty] [decimal](18, 4) NOT NULL,
	[system_qty_at_count] [decimal](18, 4) NOT NULL,
	[unit_cost] [decimal](19, 3) NOT NULL,
	[notes] [nvarchar](200) NULL,
	[created_at] [datetimeoffset](7) NOT NULL,
	[created_by] [bigint] NULL,
	[modified_at] [datetimeoffset](7) NULL,
	[modified_by] [bigint] NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_stock_count_lines] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

CREATE NONCLUSTERED INDEX [ix_stock_count_lines_stock_count_id] ON [dbo].[stock_count_lines]
(
	[stock_count_id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
CREATE UNIQUE NONCLUSTERED INDEX [ix_stock_count_lines_stock_count_id_product_id_variant_id] ON [dbo].[stock_count_lines]
(
	[stock_count_id] ASC,
	[product_id] ASC,
	[variant_id] ASC
)
WHERE ([variant_id] IS NOT NULL)
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[stock_counts](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[count_number] [bigint] NOT NULL,
	[location_id] [bigint] NOT NULL,
	[status] [nvarchar](20) NOT NULL,
	[notes] [nvarchar](500) NULL,
	[department_id] [bigint] NULL,
	[posted_at] [datetimeoffset](7) NULL,
	[created_at] [datetimeoffset](7) NOT NULL,
	[created_by] [bigint] NULL,
	[modified_at] [datetimeoffset](7) NULL,
	[modified_by] [bigint] NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_stock_counts] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

CREATE UNIQUE NONCLUSTERED INDEX [ix_stock_counts_location_id_count_number] ON [dbo].[stock_counts]
(
	[location_id] ASC,
	[count_number] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
SET ANSI_PADDING ON

CREATE NONCLUSTERED INDEX [ix_stock_counts_location_id_status] ON [dbo].[stock_counts]
(
	[location_id] ASC,
	[status] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[stock_ledger_entries](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[product_id] [bigint] NOT NULL,
	[variant_id] [bigint] NULL,
	[location_id] [bigint] NOT NULL,
	[movement_type] [nvarchar](20) NOT NULL,
	[quantity] [decimal](18, 4) NOT NULL,
	[unit_cost] [decimal](19, 3) NOT NULL,
	[reference_type] [nvarchar](max) NULL,
	[reference_id] [bigint] NULL,
	[reason] [nvarchar](max) NULL,
	[occurred_at] [datetimeoffset](7) NOT NULL,
	[staff_id] [bigint] NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_stock_ledger_entries] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

CREATE NONCLUSTERED INDEX [ix_stock_ledger_entries_product_id_location_id_occurred_at] ON [dbo].[stock_ledger_entries]
(
	[product_id] ASC,
	[location_id] ASC,
	[occurred_at] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[stock_levels](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[product_id] [bigint] NOT NULL,
	[variant_id] [bigint] NULL,
	[location_id] [bigint] NOT NULL,
	[on_hand] [decimal](18, 4) NOT NULL,
	[on_order] [decimal](18, 4) NOT NULL,
	[committed] [decimal](18, 4) NOT NULL,
	[last_sold_on] [datetimeoffset](7) NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_stock_levels] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

CREATE UNIQUE NONCLUSTERED INDEX [ix_stock_levels_product_id_variant_id_location_id] ON [dbo].[stock_levels]
(
	[product_id] ASC,
	[variant_id] ASC,
	[location_id] ASC
)
WHERE ([variant_id] IS NOT NULL)
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[stock_transfer_lines](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[stock_transfer_id] [bigint] NOT NULL,
	[product_id] [bigint] NOT NULL,
	[variant_id] [bigint] NULL,
	[stock_code] [nvarchar](30) NOT NULL,
	[product_name] [nvarchar](200) NOT NULL,
	[quantity] [decimal](18, 4) NOT NULL,
	[quantity_received] [decimal](18, 4) NOT NULL,
	[unit_cost] [decimal](19, 3) NOT NULL,
	[created_at] [datetimeoffset](7) NOT NULL,
	[created_by] [bigint] NULL,
	[modified_at] [datetimeoffset](7) NULL,
	[modified_by] [bigint] NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_stock_transfer_lines] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

CREATE NONCLUSTERED INDEX [ix_stock_transfer_lines_stock_transfer_id] ON [dbo].[stock_transfer_lines]
(
	[stock_transfer_id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
CREATE UNIQUE NONCLUSTERED INDEX [ix_stock_transfer_lines_stock_transfer_id_product_id_variant_id] ON [dbo].[stock_transfer_lines]
(
	[stock_transfer_id] ASC,
	[product_id] ASC,
	[variant_id] ASC
)
WHERE ([variant_id] IS NOT NULL)
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[stock_transfers](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[transfer_number] [bigint] NOT NULL,
	[from_location_id] [bigint] NOT NULL,
	[to_location_id] [bigint] NOT NULL,
	[status] [nvarchar](20) NOT NULL,
	[notes] [nvarchar](500) NULL,
	[shipped_at] [datetimeoffset](7) NULL,
	[received_at] [datetimeoffset](7) NULL,
	[created_at] [datetimeoffset](7) NOT NULL,
	[created_by] [bigint] NULL,
	[modified_at] [datetimeoffset](7) NULL,
	[modified_by] [bigint] NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_stock_transfers] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

SET ANSI_PADDING ON

CREATE NONCLUSTERED INDEX [ix_stock_transfers_from_location_id_status] ON [dbo].[stock_transfers]
(
	[from_location_id] ASC,
	[status] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
CREATE UNIQUE NONCLUSTERED INDEX [ix_stock_transfers_from_location_id_transfer_number] ON [dbo].[stock_transfers]
(
	[from_location_id] ASC,
	[transfer_number] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
SET ANSI_PADDING ON

CREATE NONCLUSTERED INDEX [ix_stock_transfers_to_location_id_status] ON [dbo].[stock_transfers]
(
	[to_location_id] ASC,
	[status] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[supervisor_approvals](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[permission] [nvarchar](60) NOT NULL,
	[action] [nvarchar](120) NOT NULL,
	[context] [nvarchar](500) NULL,
	[requested_by_staff_id] [bigint] NOT NULL,
	[station_id] [bigint] NOT NULL,
	[location_id] [bigint] NOT NULL,
	[status] [nvarchar](20) NOT NULL,
	[approved_by_staff_id] [bigint] NULL,
	[requested_at] [datetimeoffset](7) NOT NULL,
	[expires_at] [datetimeoffset](7) NOT NULL,
	[answered_at] [datetimeoffset](7) NULL,
	[consumed_at] [datetimeoffset](7) NULL,
	[denial_reason] [nvarchar](500) NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_supervisor_approvals] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

SET ANSI_PADDING ON

CREATE NONCLUSTERED INDEX [ix_supervisor_approvals_location_id_status_expires_at] ON [dbo].[supervisor_approvals]
(
	[location_id] ASC,
	[status] ASC,
	[expires_at] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[suppliers](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[location_id] [bigint] NOT NULL,
	[supplier_number] [nvarchar](20) NOT NULL,
	[company] [nvarchar](200) NOT NULL,
	[contact_first_name] [nvarchar](100) NULL,
	[contact_last_name] [nvarchar](100) NULL,
	[title] [nvarchar](50) NULL,
	[address_line1] [nvarchar](200) NULL,
	[address_line2] [nvarchar](200) NULL,
	[address_city] [nvarchar](100) NULL,
	[address_state_or_province] [nvarchar](100) NULL,
	[address_postal_code] [nvarchar](20) NULL,
	[address_country] [nvarchar](100) NULL,
	[contact_phone] [nvarchar](30) NULL,
	[contact_extension] [nvarchar](10) NULL,
	[contact_mobile] [nvarchar](30) NULL,
	[contact_fax] [nvarchar](30) NULL,
	[contact_email] [nvarchar](200) NULL,
	[contact_website] [nvarchar](200) NULL,
	[is_deleted] [bit] NOT NULL,
	[deleted_at] [datetimeoffset](7) NULL,
	[deleted_by] [bigint] NULL,
	[created_at] [datetimeoffset](7) NOT NULL,
	[created_by] [bigint] NULL,
	[modified_at] [datetimeoffset](7) NULL,
	[modified_by] [bigint] NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_suppliers] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[sync_logs](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[provider] [nvarchar](max) NOT NULL,
	[direction] [int] NOT NULL,
	[entity] [nvarchar](max) NOT NULL,
	[request_payload] [nvarchar](max) NULL,
	[response_payload] [nvarchar](max) NULL,
	[status] [int] NOT NULL,
	[error_message] [nvarchar](max) NULL,
	[record_count] [int] NOT NULL,
	[occurred_at] [datetimeoffset](7) NOT NULL,
	[duration_ms] [bigint] NOT NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_sync_logs] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[tax_configurations](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[location_id] [bigint] NOT NULL,
	[effective_from] [date] NOT NULL,
	[effective_to] [date] NULL,
	[tax1enabled] [bit] NOT NULL,
	[tax1name] [nvarchar](50) NOT NULL,
	[tax1rate] [decimal](7, 4) NOT NULL,
	[tax2enabled] [bit] NOT NULL,
	[tax2name] [nvarchar](50) NOT NULL,
	[tax2rate] [decimal](7, 4) NOT NULL,
	[tax2compound] [bit] NOT NULL,
	[add_on_charge_enabled] [bit] NOT NULL,
	[add_on_charge_name] [nvarchar](50) NOT NULL,
	[add_on_charge_rate] [decimal](7, 4) NOT NULL,
	[add_on_charge_taxable] [bit] NOT NULL,
	[taxation_type] [nvarchar](20) NOT NULL,
	[registration_number] [nvarchar](50) NULL,
	[created_at] [datetimeoffset](7) NOT NULL,
	[created_by] [bigint] NULL,
	[modified_at] [datetimeoffset](7) NULL,
	[modified_by] [bigint] NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_tax_configurations] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

CREATE NONCLUSTERED INDEX [ix_tax_configurations_location_id_effective_from] ON [dbo].[tax_configurations]
(
	[location_id] DESC,
	[effective_from] DESC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[tender_types](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[code] [nvarchar](max) NOT NULL,
	[display_name] [nvarchar](max) NOT NULL,
	[behaviour] [int] NOT NULL,
	[sort_order] [int] NOT NULL,
	[icon_key] [nvarchar](max) NULL,
	[opens_cash_drawer] [bit] NOT NULL,
	[allows_over_tender] [bit] NOT NULL,
	[rounds_to_minimum_tender] [bit] NOT NULL,
	[counts_towards_drawer_cash] [bit] NOT NULL,
	[requires_reference] [bit] NOT NULL,
	[prints_signature_copy] [bit] NOT NULL,
	[allowed_for_refunds] [bit] NOT NULL,
	[currency_code] [nvarchar](max) NULL,
	[external_accounting_key] [nvarchar](max) NULL,
	[is_active] [bit] NOT NULL,
	[is_deleted] [bit] NOT NULL,
	[deleted_at] [datetimeoffset](7) NULL,
	[deleted_by] [bigint] NULL,
	[created_at] [datetimeoffset](7) NOT NULL,
	[created_by] [bigint] NULL,
	[modified_at] [datetimeoffset](7) NULL,
	[modified_by] [bigint] NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_tender_types] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[time_clock_entries](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[staff_id] [bigint] NOT NULL,
	[location_id] [bigint] NOT NULL,
	[clock_in] [datetimeoffset](7) NOT NULL,
	[clock_out] [datetimeoffset](7) NULL,
	[hours_worked] [decimal](9, 4) NULL,
	[created_at] [datetimeoffset](7) NOT NULL,
	[created_by] [bigint] NULL,
	[modified_at] [datetimeoffset](7) NULL,
	[modified_by] [bigint] NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_time_clock_entries] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

CREATE NONCLUSTERED INDEX [ix_time_clock_entries_location_id_clock_in] ON [dbo].[time_clock_entries]
(
	[location_id] ASC,
	[clock_in] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
CREATE NONCLUSTERED INDEX [ix_time_clock_entries_staff_id_clock_in] ON [dbo].[time_clock_entries]
(
	[staff_id] ASC,
	[clock_in] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[trolley_sessions](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[trolley_id] [bigint] NOT NULL,
	[shopper_id] [bigint] NOT NULL,
	[cart_id] [bigint] NOT NULL,
	[location_id] [bigint] NOT NULL,
	[state] [nvarchar](20) NOT NULL,
	[claimed_at] [datetimeoffset](7) NOT NULL,
	[last_activity_at] [datetimeoffset](7) NOT NULL,
	[ended_at] [datetimeoffset](7) NULL,
	[sale_id] [bigint] NULL,
	[created_at] [datetimeoffset](7) NOT NULL,
	[created_by] [bigint] NULL,
	[modified_at] [datetimeoffset](7) NULL,
	[modified_by] [bigint] NULL,
	[row_version] [bigint] NOT NULL,
 CONSTRAINT [pk_trolley_sessions] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

CREATE NONCLUSTERED INDEX [ix_trolley_sessions_cart_id] ON [dbo].[trolley_sessions]
(
	[cart_id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
SET ANSI_PADDING ON

CREATE UNIQUE NONCLUSTERED INDEX [ix_trolley_sessions_shopper_id_state] ON [dbo].[trolley_sessions]
(
	[shopper_id] ASC,
	[state] ASC
)
WHERE ([state]='Shopping')
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
SET ANSI_PADDING ON

CREATE NONCLUSTERED INDEX [ix_trolley_sessions_state_last_activity_at] ON [dbo].[trolley_sessions]
(
	[state] ASC,
	[last_activity_at] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
SET ANSI_PADDING ON

CREATE UNIQUE NONCLUSTERED INDEX [ix_trolley_sessions_trolley_id_state] ON [dbo].[trolley_sessions]
(
	[trolley_id] ASC,
	[state] ASC
)
WHERE ([state]='Shopping')
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [dbo].[trolleys](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[code] [nvarchar](6) NOT NULL,
	[location_id] [bigint] NOT NULL,
	[station_id] [bigint] NOT NULL,
	[label] [nvarchar](100) NULL,
	[is_active] [bit] NOT NULL,
	[created_at] [datetimeoffset](7) NOT NULL,
	[created_by] [bigint] NULL,
	[modified_at] [datetimeoffset](7) NULL,
	[modified_by] [bigint] NULL,
	[row_version] [bigint] NOT NULL,
	[tare_weight_kg] [decimal](6, 3) NULL,
 CONSTRAINT [pk_trolleys] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

SET ANSI_PADDING ON

CREATE UNIQUE NONCLUSTERED INDEX [ix_trolleys_location_id_code] ON [dbo].[trolleys]
(
	[location_id] ASC,
	[code] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
CREATE UNIQUE NONCLUSTERED INDEX [ix_trolleys_station_id] ON [dbo].[trolleys]
(
	[station_id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
ALTER TABLE [dbo].[trolleys]  WITH CHECK ADD  CONSTRAINT [fk_trolleys_stations_station_id] FOREIGN KEY([station_id])
REFERENCES [dbo].[stations] ([id])
ALTER TABLE [dbo].[trolleys] CHECK CONSTRAINT [fk_trolleys_stations_station_id]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [HangFire].[AggregatedCounter](
	[Key] [nvarchar](100) NOT NULL,
	[Value] [bigint] NOT NULL,
	[ExpireAt] [datetime] NULL,
 CONSTRAINT [PK_HangFire_CounterAggregated] PRIMARY KEY CLUSTERED 
(
	[Key] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

CREATE NONCLUSTERED INDEX [IX_HangFire_AggregatedCounter_ExpireAt] ON [HangFire].[AggregatedCounter]
(
	[ExpireAt] ASC
)
WHERE ([ExpireAt] IS NOT NULL)
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [HangFire].[Counter](
	[Key] [nvarchar](100) NOT NULL,
	[Value] [int] NOT NULL,
	[ExpireAt] [datetime] NULL,
	[Id] [bigint] IDENTITY(1,1) NOT NULL,
 CONSTRAINT [PK_HangFire_Counter] PRIMARY KEY CLUSTERED 
(
	[Key] ASC,
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [HangFire].[Hash](
	[Key] [nvarchar](100) NOT NULL,
	[Field] [nvarchar](100) NOT NULL,
	[Value] [nvarchar](max) NULL,
	[ExpireAt] [datetime2](7) NULL,
 CONSTRAINT [PK_HangFire_Hash] PRIMARY KEY CLUSTERED 
(
	[Key] ASC,
	[Field] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = ON, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

CREATE NONCLUSTERED INDEX [IX_HangFire_Hash_ExpireAt] ON [HangFire].[Hash]
(
	[ExpireAt] ASC
)
WHERE ([ExpireAt] IS NOT NULL)
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [HangFire].[Job](
	[Id] [bigint] IDENTITY(1,1) NOT NULL,
	[StateId] [bigint] NULL,
	[StateName] [nvarchar](20) NULL,
	[InvocationData] [nvarchar](max) NOT NULL,
	[Arguments] [nvarchar](max) NOT NULL,
	[CreatedAt] [datetime] NOT NULL,
	[ExpireAt] [datetime] NULL,
 CONSTRAINT [PK_HangFire_Job] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

CREATE NONCLUSTERED INDEX [IX_HangFire_Job_ExpireAt] ON [HangFire].[Job]
(
	[ExpireAt] ASC
)
INCLUDE([StateName]) 
WHERE ([ExpireAt] IS NOT NULL)
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
SET ANSI_PADDING ON

CREATE NONCLUSTERED INDEX [IX_HangFire_Job_StateName] ON [HangFire].[Job]
(
	[StateName] ASC
)
WHERE ([StateName] IS NOT NULL)
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [HangFire].[JobParameter](
	[JobId] [bigint] NOT NULL,
	[Name] [nvarchar](40) NOT NULL,
	[Value] [nvarchar](max) NULL,
 CONSTRAINT [PK_HangFire_JobParameter] PRIMARY KEY CLUSTERED 
(
	[JobId] ASC,
	[Name] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

ALTER TABLE [HangFire].[JobParameter]  WITH CHECK ADD  CONSTRAINT [FK_HangFire_JobParameter_Job] FOREIGN KEY([JobId])
REFERENCES [HangFire].[Job] ([Id])
ON UPDATE CASCADE
ON DELETE CASCADE
ALTER TABLE [HangFire].[JobParameter] CHECK CONSTRAINT [FK_HangFire_JobParameter_Job]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [HangFire].[JobQueue](
	[Id] [bigint] IDENTITY(1,1) NOT NULL,
	[JobId] [bigint] NOT NULL,
	[Queue] [nvarchar](50) NOT NULL,
	[FetchedAt] [datetime] NULL,
 CONSTRAINT [PK_HangFire_JobQueue] PRIMARY KEY CLUSTERED 
(
	[Queue] ASC,
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [HangFire].[List](
	[Id] [bigint] IDENTITY(1,1) NOT NULL,
	[Key] [nvarchar](100) NOT NULL,
	[Value] [nvarchar](max) NULL,
	[ExpireAt] [datetime] NULL,
 CONSTRAINT [PK_HangFire_List] PRIMARY KEY CLUSTERED 
(
	[Key] ASC,
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

CREATE NONCLUSTERED INDEX [IX_HangFire_List_ExpireAt] ON [HangFire].[List]
(
	[ExpireAt] ASC
)
WHERE ([ExpireAt] IS NOT NULL)
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [HangFire].[Schema](
	[Version] [int] NOT NULL,
 CONSTRAINT [PK_HangFire_Schema] PRIMARY KEY CLUSTERED 
(
	[Version] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [HangFire].[Server](
	[Id] [nvarchar](200) NOT NULL,
	[Data] [nvarchar](max) NULL,
	[LastHeartbeat] [datetime] NOT NULL,
 CONSTRAINT [PK_HangFire_Server] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

CREATE NONCLUSTERED INDEX [IX_HangFire_Server_LastHeartbeat] ON [HangFire].[Server]
(
	[LastHeartbeat] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [HangFire].[Set](
	[Key] [nvarchar](100) NOT NULL,
	[Score] [float] NOT NULL,
	[Value] [nvarchar](256) NOT NULL,
	[ExpireAt] [datetime] NULL,
 CONSTRAINT [PK_HangFire_Set] PRIMARY KEY CLUSTERED 
(
	[Key] ASC,
	[Value] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = ON, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]

CREATE NONCLUSTERED INDEX [IX_HangFire_Set_ExpireAt] ON [HangFire].[Set]
(
	[ExpireAt] ASC
)
WHERE ([ExpireAt] IS NOT NULL)
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
SET ANSI_PADDING ON

CREATE NONCLUSTERED INDEX [IX_HangFire_Set_Score] ON [HangFire].[Set]
(
	[Key] ASC,
	[Score] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_NULLS ON
SET QUOTED_IDENTIFIER ON
CREATE TABLE [HangFire].[State](
	[Id] [bigint] IDENTITY(1,1) NOT NULL,
	[JobId] [bigint] NOT NULL,
	[Name] [nvarchar](20) NOT NULL,
	[Reason] [nvarchar](100) NULL,
	[CreatedAt] [datetime] NOT NULL,
	[Data] [nvarchar](max) NULL,
 CONSTRAINT [PK_HangFire_State] PRIMARY KEY CLUSTERED 
(
	[JobId] ASC,
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

CREATE NONCLUSTERED INDEX [IX_HangFire_State_CreatedAt] ON [HangFire].[State]
(
	[CreatedAt] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
ALTER TABLE [HangFire].[State]  WITH CHECK ADD  CONSTRAINT [FK_HangFire_State_Job] FOREIGN KEY([JobId])
REFERENCES [HangFire].[Job] ([Id])
ON UPDATE CASCADE
ON DELETE CASCADE
ALTER TABLE [HangFire].[State] CHECK CONSTRAINT [FK_HangFire_State_Job]
GO

/* ====================================================================================
   VIEWS  (1)
   ==================================================================================== */


/* ====================================================================================
   FUNCTIONS  (1)
   ==================================================================================== */


/* ====================================================================================
   STORED PROCEDURES  (1)
   ==================================================================================== */

