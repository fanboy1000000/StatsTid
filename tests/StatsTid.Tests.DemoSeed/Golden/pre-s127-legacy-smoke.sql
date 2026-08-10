-- ============================================================================
-- 99-demo-seed.sql — OPT-IN realistic demo dataset (S84). GENERATED ARTIFACT.
-- Produced deterministically by tools/StatsTid.DemoSeed (fixed seed). Do not hand-edit.
-- Loaded ONLY via the demo compose overlay (docker/docker-compose.demo.yml), on a
-- FRESH postgres volume. The overlay mounts this as zz-demo-seed.sql so it sorts AFTER
-- init.sql (the entrypoint runs files in byte-lexical order; the on-disk 99- name would
-- sort BEFORE init.sql). NEVER mounted in CI.
-- The reporting TREES + activity are loaded post-boot via the StatsTid.DemoSeed API
-- loader (event-emitting). This file carries orgs + users + bulk EMPLOYEE role_assignments
-- + the privileged LOCAL_HR/LOCAL_LEADER rows (SQL-seeded, event-less — the roles/grant
-- API has a product defect; see SPRINT-84) + a demo GLOBAL_ADMIN bootstrap.
--
-- scale=smoke  seed=42  referenceDate=2026-06-15
-- orgs=2  users=30 (+1 demo_admin)  employeeRoles=30  privilegedRoles=5
-- ============================================================================

-- ── Organisations (S92 / ADR-035 flatten: 1 demo MAO root + N ORGANISATIONs under it; ──
--    no AFDELING/TEAM org rows — deep structure lives in `units` from S104 / Phase 3) ──
INSERT INTO organizations (org_id, org_name, org_type, parent_org_id, materialized_path, agreement_code, ok_version, is_active) VALUES
    ('MINX', 'Demoministeriet', 'MAO', NULL, '/MINX/', 'AC', 'OK24', TRUE),
    ('STYX1', 'Demostyrelsen (smoke)', 'ORGANISATION', 'MINX', '/MINX/STYX1/', 'AC', 'OK24', TRUE)
ON CONFLICT DO NOTHING;

-- ── Demo GLOBAL_ADMIN bootstrap user (the loader authenticates as this) ──
INSERT INTO users (user_id, username, password_hash, display_name, email, primary_org_id, agreement_code, ok_version, employment_category, is_active) VALUES
    ('demo_admin', 'demo_admin', '$2a$11$9d/J80pl7VKKjtWsSqJdPuvJqBL/3sYomNGgL.TdUKq2Aw0e6k0Te', 'Demo Global Admin', 'demo_admin@demo.dk', 'MINX', 'AC', 'OK24', 'Kontorchef', TRUE)
ON CONFLICT DO NOTHING;

-- ── Demo users (real users columns only; part_time_fraction/position set via the profile API) ──
INSERT INTO users (user_id, username, password_hash, display_name, email, primary_org_id, agreement_code, ok_version, employment_category, birth_date, employment_start_date, employment_end_date, is_active) VALUES
    ('demo_styx1_0001', 'demo_styx1_0001', '$2a$11$9d/J80pl7VKKjtWsSqJdPuvJqBL/3sYomNGgL.TdUKq2Aw0e6k0Te', 'Emil Christensen', 'emil.christensen.demo_styx1_0001@demo.dk', 'STYX1', 'AC', 'OK24', 'Kontorchef', DATE '1974-09-18', DATE '2003-12-11', NULL, TRUE),
    ('demo_styx1_0002', 'demo_styx1_0002', '$2a$11$9d/J80pl7VKKjtWsSqJdPuvJqBL/3sYomNGgL.TdUKq2Aw0e6k0Te', 'Pernille Berg', 'pernille.berg.demo_styx1_0002@demo.dk', 'STYX1', 'AC', 'OK24', 'Chefkonsulent', DATE '1988-09-17', DATE '2010-06-03', NULL, TRUE),
    ('demo_styx1_0003', 'demo_styx1_0003', '$2a$11$9d/J80pl7VKKjtWsSqJdPuvJqBL/3sYomNGgL.TdUKq2Aw0e6k0Te', 'Simon Christiansen', 'simon.christiansen.demo_styx1_0003@demo.dk', 'STYX1', 'AC', 'OK24', 'Kontorchef', DATE '1983-07-01', DATE '2004-04-23', NULL, TRUE),
    ('demo_styx1_0004', 'demo_styx1_0004', '$2a$11$9d/J80pl7VKKjtWsSqJdPuvJqBL/3sYomNGgL.TdUKq2Aw0e6k0Te', 'Andreas Moeller', 'andreas.moeller.demo_styx1_0004@demo.dk', 'STYX1', 'AC', 'OK26', 'Specialkonsulent', DATE '1973-06-16', DATE '2009-08-26', NULL, TRUE),
    ('demo_styx1_0005', 'demo_styx1_0005', '$2a$11$9d/J80pl7VKKjtWsSqJdPuvJqBL/3sYomNGgL.TdUKq2Aw0e6k0Te', 'Rasmus Christensen', 'rasmus.christensen.demo_styx1_0005@demo.dk', 'STYX1', 'AC', 'OK26', 'Kontorchef', DATE '1997-09-14', DATE '2003-03-02', NULL, TRUE),
    ('demo_styx1_0006', 'demo_styx1_0006', '$2a$11$9d/J80pl7VKKjtWsSqJdPuvJqBL/3sYomNGgL.TdUKq2Aw0e6k0Te', 'Maria Nielsen', 'maria.nielsen.demo_styx1_0006@demo.dk', 'STYX1', 'HK', 'OK24', 'Fuldmaegtig', DATE '1978-01-30', DATE '2020-06-11', NULL, TRUE),
    ('demo_styx1_0007', 'demo_styx1_0007', '$2a$11$9d/J80pl7VKKjtWsSqJdPuvJqBL/3sYomNGgL.TdUKq2Aw0e6k0Te', 'Lene Hansen', 'lene.hansen.demo_styx1_0007@demo.dk', 'STYX1', 'HK', 'OK26', 'Chefkonsulent', DATE '1960-11-22', DATE '2010-12-15', DATE '2025-10-08', FALSE),
    ('demo_styx1_0008', 'demo_styx1_0008', '$2a$11$9d/J80pl7VKKjtWsSqJdPuvJqBL/3sYomNGgL.TdUKq2Aw0e6k0Te', 'Anders Jensen', 'anders.jensen.demo_styx1_0008@demo.dk', 'STYX1', 'AC', 'OK24', 'Fuldmaegtig', DATE '1969-11-30', DATE '2014-11-02', NULL, TRUE),
    ('demo_styx1_0009', 'demo_styx1_0009', '$2a$11$9d/J80pl7VKKjtWsSqJdPuvJqBL/3sYomNGgL.TdUKq2Aw0e6k0Te', 'Bente Christensen', 'bente.christensen.demo_styx1_0009@demo.dk', 'STYX1', 'AC', 'OK24', 'Chefkonsulent', DATE '1992-05-11', DATE '2007-09-21', DATE '2025-09-23', FALSE),
    ('demo_styx1_0010', 'demo_styx1_0010', '$2a$11$9d/J80pl7VKKjtWsSqJdPuvJqBL/3sYomNGgL.TdUKq2Aw0e6k0Te', 'Eva Olsen', 'eva.olsen.demo_styx1_0010@demo.dk', 'STYX1', 'HK', 'OK24', 'Fuldmaegtig', DATE '1991-03-25', DATE '2022-04-27', NULL, TRUE),
    ('demo_styx1_0011', 'demo_styx1_0011', '$2a$11$9d/J80pl7VKKjtWsSqJdPuvJqBL/3sYomNGgL.TdUKq2Aw0e6k0Te', 'Marianne Moeller', 'marianne.moeller.demo_styx1_0011@demo.dk', 'STYX1', 'PROSA', 'OK24', 'Kontorchef', DATE '1992-01-02', DATE '1995-10-22', DATE '2026-02-25', FALSE),
    ('demo_styx1_0012', 'demo_styx1_0012', '$2a$11$9d/J80pl7VKKjtWsSqJdPuvJqBL/3sYomNGgL.TdUKq2Aw0e6k0Te', 'Laura Nielsen', 'laura.nielsen.demo_styx1_0012@demo.dk', 'STYX1', 'AC', 'OK24', 'Fuldmaegtig', DATE '1981-11-16', DATE '2023-06-19', NULL, TRUE),
    ('demo_styx1_0013', 'demo_styx1_0013', '$2a$11$9d/J80pl7VKKjtWsSqJdPuvJqBL/3sYomNGgL.TdUKq2Aw0e6k0Te', 'Henrik Kristensen', 'henrik.kristensen.demo_styx1_0013@demo.dk', 'STYX1', 'AC', 'OK24', 'Standard', DATE '1967-04-17', DATE '1999-03-21', NULL, TRUE),
    ('demo_styx1_0014', 'demo_styx1_0014', '$2a$11$9d/J80pl7VKKjtWsSqJdPuvJqBL/3sYomNGgL.TdUKq2Aw0e6k0Te', 'William Madsen', 'william.madsen.demo_styx1_0014@demo.dk', 'STYX1', 'AC', 'OK24', 'Kontorchef', DATE '1991-08-20', DATE '2026-03-23', NULL, TRUE),
    ('demo_styx1_0015', 'demo_styx1_0015', '$2a$11$9d/J80pl7VKKjtWsSqJdPuvJqBL/3sYomNGgL.TdUKq2Aw0e6k0Te', 'Else Dahl', 'else.dahl.demo_styx1_0015@demo.dk', 'STYX1', 'AC', 'OK24', 'Fuldmaegtig', DATE '1983-01-04', DATE '2004-05-02', NULL, TRUE),
    ('demo_styx1_0016', 'demo_styx1_0016', '$2a$11$9d/J80pl7VKKjtWsSqJdPuvJqBL/3sYomNGgL.TdUKq2Aw0e6k0Te', 'Karen Bruun', 'karen.bruun.demo_styx1_0016@demo.dk', 'STYX1', 'AC', 'OK24', 'Standard', DATE '1983-05-26', DATE '2001-04-30', NULL, TRUE),
    ('demo_styx1_0017', 'demo_styx1_0017', '$2a$11$9d/J80pl7VKKjtWsSqJdPuvJqBL/3sYomNGgL.TdUKq2Aw0e6k0Te', 'Anders Soerensen', 'anders.soerensen.demo_styx1_0017@demo.dk', 'STYX1', 'PROSA', 'OK24', 'Chefkonsulent', DATE '1987-07-30', DATE '1997-06-29', NULL, TRUE),
    ('demo_styx1_0018', 'demo_styx1_0018', '$2a$11$9d/J80pl7VKKjtWsSqJdPuvJqBL/3sYomNGgL.TdUKq2Aw0e6k0Te', 'Pernille Olsen', 'pernille.olsen.demo_styx1_0018@demo.dk', 'STYX1', 'AC', 'OK24', 'Standard', DATE '1975-09-25', DATE '2015-12-07', NULL, TRUE),
    ('demo_styx1_0019', 'demo_styx1_0019', '$2a$11$9d/J80pl7VKKjtWsSqJdPuvJqBL/3sYomNGgL.TdUKq2Aw0e6k0Te', 'Susanne Bruun', 'susanne.bruun.demo_styx1_0019@demo.dk', 'STYX1', 'HK', 'OK24', 'Specialkonsulent', DATE '1973-02-03', DATE '2024-01-14', NULL, TRUE),
    ('demo_styx1_0020', 'demo_styx1_0020', '$2a$11$9d/J80pl7VKKjtWsSqJdPuvJqBL/3sYomNGgL.TdUKq2Aw0e6k0Te', 'Noah Dahl', 'noah.dahl.demo_styx1_0020@demo.dk', 'STYX1', 'PROSA', 'OK24', 'Chefkonsulent', DATE '1968-08-11', DATE '2008-03-30', NULL, TRUE),
    ('demo_styx1_0021', 'demo_styx1_0021', '$2a$11$9d/J80pl7VKKjtWsSqJdPuvJqBL/3sYomNGgL.TdUKq2Aw0e6k0Te', 'Frederik Schmidt', 'frederik.schmidt.demo_styx1_0021@demo.dk', 'STYX1', 'AC', 'OK24', 'Specialkonsulent', DATE '1976-08-27', DATE '2009-09-01', NULL, TRUE),
    ('demo_styx1_0022', 'demo_styx1_0022', '$2a$11$9d/J80pl7VKKjtWsSqJdPuvJqBL/3sYomNGgL.TdUKq2Aw0e6k0Te', 'Jakob Soendergaard', 'jakob.soendergaard.demo_styx1_0022@demo.dk', 'STYX1', 'AC', 'OK26', 'Specialkonsulent', DATE '1995-09-29', DATE '2017-07-29', NULL, TRUE),
    ('demo_styx1_0023', 'demo_styx1_0023', '$2a$11$9d/J80pl7VKKjtWsSqJdPuvJqBL/3sYomNGgL.TdUKq2Aw0e6k0Te', 'Bente Knudsen', 'bente.knudsen.demo_styx1_0023@demo.dk', 'STYX1', 'HK', 'OK24', 'Standard', DATE '1972-10-06', DATE '2020-07-08', NULL, TRUE),
    ('demo_styx1_0024', 'demo_styx1_0024', '$2a$11$9d/J80pl7VKKjtWsSqJdPuvJqBL/3sYomNGgL.TdUKq2Aw0e6k0Te', 'Lone Christiansen', 'lone.christiansen.demo_styx1_0024@demo.dk', 'STYX1', 'PROSA', 'OK26', 'Chefkonsulent', DATE '1992-05-15', DATE '1998-07-25', DATE '2026-03-11', FALSE),
    ('demo_styx1_0025', 'demo_styx1_0025', '$2a$11$9d/J80pl7VKKjtWsSqJdPuvJqBL/3sYomNGgL.TdUKq2Aw0e6k0Te', 'Marianne Jensen', 'marianne.jensen.demo_styx1_0025@demo.dk', 'STYX1', 'HK', 'OK24', 'Kontorchef', DATE '1967-10-01', DATE '1996-09-14', NULL, TRUE),
    ('demo_styx1_0026', 'demo_styx1_0026', '$2a$11$9d/J80pl7VKKjtWsSqJdPuvJqBL/3sYomNGgL.TdUKq2Aw0e6k0Te', 'Tina Laursen', 'tina.laursen.demo_styx1_0026@demo.dk', 'STYX1', 'AC', 'OK24', 'Chefkonsulent', DATE '1989-09-02', DATE '2007-10-25', NULL, TRUE),
    ('demo_styx1_0027', 'demo_styx1_0027', '$2a$11$9d/J80pl7VKKjtWsSqJdPuvJqBL/3sYomNGgL.TdUKq2Aw0e6k0Te', 'Jesper Laursen', 'jesper.laursen.demo_styx1_0027@demo.dk', 'STYX1', 'AC', 'OK24', 'Standard', DATE '1967-10-21', DATE '2025-11-16', NULL, TRUE),
    ('demo_styx1_0028', 'demo_styx1_0028', '$2a$11$9d/J80pl7VKKjtWsSqJdPuvJqBL/3sYomNGgL.TdUKq2Aw0e6k0Te', 'Maria Soerensen', 'maria.soerensen.demo_styx1_0028@demo.dk', 'STYX1', 'HK', 'OK24', 'Standard', DATE '1979-08-26', DATE '2006-10-20', NULL, TRUE),
    ('demo_styx1_0029', 'demo_styx1_0029', '$2a$11$9d/J80pl7VKKjtWsSqJdPuvJqBL/3sYomNGgL.TdUKq2Aw0e6k0Te', 'Niels Mikkelsen', 'niels.mikkelsen.demo_styx1_0029@demo.dk', 'STYX1', 'AC', 'OK24', 'Fuldmaegtig', DATE '1982-12-28', DATE '2006-01-16', NULL, TRUE),
    ('demo_styx1_0030', 'demo_styx1_0030', '$2a$11$9d/J80pl7VKKjtWsSqJdPuvJqBL/3sYomNGgL.TdUKq2Aw0e6k0Te', 'Klaus Lund', 'klaus.lund.demo_styx1_0030@demo.dk', 'STYX1', 'AC', 'OK24', 'Chefkonsulent', DATE '1977-10-08', DATE '2007-09-15', NULL, TRUE)
ON CONFLICT DO NOTHING;

-- ── Demo GLOBAL_ADMIN role assignment (GLOBAL scope) ──
INSERT INTO role_assignments (user_id, role_id, org_id, scope_type, assigned_by) VALUES
    ('demo_admin', 'GLOBAL_ADMIN', NULL, 'GLOBAL', 'DEMO_SEED')
ON CONFLICT DO NOTHING;

-- ── Privileged LOCAL_HR / LOCAL_LEADER role_assignments (event-less; SQL-seeded because
--    POST /api/admin/roles/grant has a pre-existing schema bug — see SPRINT-84) ──
INSERT INTO role_assignments (user_id, role_id, org_id, scope_type, assigned_by) VALUES
    ('demo_styx1_0001', 'LOCAL_HR', 'STYX1', 'ORG_ONLY', 'DEMO_SEED'),
    ('demo_styx1_0001', 'LOCAL_LEADER', 'STYX1', 'ORG_ONLY', 'DEMO_SEED'),
    ('demo_styx1_0002', 'LOCAL_LEADER', 'STYX1', 'ORG_ONLY', 'DEMO_SEED'),
    ('demo_styx1_0003', 'LOCAL_LEADER', 'STYX1', 'ORG_ONLY', 'DEMO_SEED'),
    ('demo_styx1_0004', 'LOCAL_LEADER', 'STYX1', 'ORG_ONLY', 'DEMO_SEED')
ON CONFLICT DO NOTHING;

-- ── Bulk EMPLOYEE role_assignments (event-less by design; assigned_by='DEMO_SEED') ──
INSERT INTO role_assignments (user_id, role_id, org_id, scope_type, assigned_by) VALUES
    ('demo_styx1_0001', 'EMPLOYEE', 'STYX1', 'ORG_ONLY', 'DEMO_SEED'),
    ('demo_styx1_0002', 'EMPLOYEE', 'STYX1', 'ORG_ONLY', 'DEMO_SEED'),
    ('demo_styx1_0003', 'EMPLOYEE', 'STYX1', 'ORG_ONLY', 'DEMO_SEED'),
    ('demo_styx1_0004', 'EMPLOYEE', 'STYX1', 'ORG_ONLY', 'DEMO_SEED'),
    ('demo_styx1_0005', 'EMPLOYEE', 'STYX1', 'ORG_ONLY', 'DEMO_SEED'),
    ('demo_styx1_0006', 'EMPLOYEE', 'STYX1', 'ORG_ONLY', 'DEMO_SEED'),
    ('demo_styx1_0007', 'EMPLOYEE', 'STYX1', 'ORG_ONLY', 'DEMO_SEED'),
    ('demo_styx1_0008', 'EMPLOYEE', 'STYX1', 'ORG_ONLY', 'DEMO_SEED'),
    ('demo_styx1_0009', 'EMPLOYEE', 'STYX1', 'ORG_ONLY', 'DEMO_SEED'),
    ('demo_styx1_0010', 'EMPLOYEE', 'STYX1', 'ORG_ONLY', 'DEMO_SEED'),
    ('demo_styx1_0011', 'EMPLOYEE', 'STYX1', 'ORG_ONLY', 'DEMO_SEED'),
    ('demo_styx1_0012', 'EMPLOYEE', 'STYX1', 'ORG_ONLY', 'DEMO_SEED'),
    ('demo_styx1_0013', 'EMPLOYEE', 'STYX1', 'ORG_ONLY', 'DEMO_SEED'),
    ('demo_styx1_0014', 'EMPLOYEE', 'STYX1', 'ORG_ONLY', 'DEMO_SEED'),
    ('demo_styx1_0015', 'EMPLOYEE', 'STYX1', 'ORG_ONLY', 'DEMO_SEED'),
    ('demo_styx1_0016', 'EMPLOYEE', 'STYX1', 'ORG_ONLY', 'DEMO_SEED'),
    ('demo_styx1_0017', 'EMPLOYEE', 'STYX1', 'ORG_ONLY', 'DEMO_SEED'),
    ('demo_styx1_0018', 'EMPLOYEE', 'STYX1', 'ORG_ONLY', 'DEMO_SEED'),
    ('demo_styx1_0019', 'EMPLOYEE', 'STYX1', 'ORG_ONLY', 'DEMO_SEED'),
    ('demo_styx1_0020', 'EMPLOYEE', 'STYX1', 'ORG_ONLY', 'DEMO_SEED'),
    ('demo_styx1_0021', 'EMPLOYEE', 'STYX1', 'ORG_ONLY', 'DEMO_SEED'),
    ('demo_styx1_0022', 'EMPLOYEE', 'STYX1', 'ORG_ONLY', 'DEMO_SEED'),
    ('demo_styx1_0023', 'EMPLOYEE', 'STYX1', 'ORG_ONLY', 'DEMO_SEED'),
    ('demo_styx1_0024', 'EMPLOYEE', 'STYX1', 'ORG_ONLY', 'DEMO_SEED'),
    ('demo_styx1_0025', 'EMPLOYEE', 'STYX1', 'ORG_ONLY', 'DEMO_SEED'),
    ('demo_styx1_0026', 'EMPLOYEE', 'STYX1', 'ORG_ONLY', 'DEMO_SEED'),
    ('demo_styx1_0027', 'EMPLOYEE', 'STYX1', 'ORG_ONLY', 'DEMO_SEED'),
    ('demo_styx1_0028', 'EMPLOYEE', 'STYX1', 'ORG_ONLY', 'DEMO_SEED'),
    ('demo_styx1_0029', 'EMPLOYEE', 'STYX1', 'ORG_ONLY', 'DEMO_SEED'),
    ('demo_styx1_0030', 'EMPLOYEE', 'STYX1', 'ORG_ONLY', 'DEMO_SEED')
ON CONFLICT DO NOTHING;

-- ============================================================================
-- End of generated demo seed.
-- ============================================================================
