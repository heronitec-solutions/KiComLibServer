--
-- KiCADDbL library seeder script
--

-- Fills the database with the initial library files and libraries


--------------------------------------------------------------------------------------------------
-- ADD LIBRARY FILES
--------------------------------------------------------------------------------------------------

INSERT INTO public.library_file (uuid, type, name, filename, description, version) VALUES
    ('f74e5a3d-f74f-4d53-a778-5c41692b5af1', 0, 'KiComLib Footprints', 'KiComLib_Footprints', '', '20260206'),
    ('d10a5831-b813-49a6-9939-cb3117dfd70f', 1, 'KiComLib Symbols', 'KiComLib_Symbols', '', '20251024');



--------------------------------------------------------------------------------------------------
-- ADD LIBRARIES
--------------------------------------------------------------------------------------------------

INSERT INTO public.library (uuid, name, filename, description) VALUES
    ('00b1062c-f2ac-454a-9c4a-5671410faf52', 'KiComLib C', 'KiComLib_C', 'KiComLib Capacitors'),
    ('579335d9-7eb6-4630-9f65-6a331dc4d4f8', 'KiComLib R', 'KiComLib_R', 'KiComLib Resistors'),
    ('381fdc55-20d7-4e5c-96c9-6343e4430020', 'KiComLib D', 'KiComLib_D', 'KiComLib Diodes'),
    ('324b2a3f-d168-4b86-b5f9-f788fb5fddef', 'KiComLib L', 'KiComLib_L', 'KiComLib Impedance'),
    ('cf0f5241-d200-44a4-9fb5-0d91f35526b3', 'KiComLib J', 'KiComLib_J', 'KiComLib Connectors'),
    ('1796be5e-f7a0-4e00-9b18-b123d74ed1d8', 'KiComLib IC', 'KiComLib_IC', 'KiComLib Integrated Circuits'),
    ('c70fc1bb-100d-460e-916d-1d44620de02c', 'KiComLib Graphics', 'KiComLib_Graphics', 'KiComLib Symbols / Logos'),
    ('810784d6-fd47-4d27-806e-04871843337b', 'KiComLib Misc', 'KiComLib_Misc', 'KiComLib Miscellaneous'),
    ('6e034dcd-885a-48c4-b7ed-20aed3912e80', 'KiComLib Modules', 'KiComLib_Modules', 'KiComLib Modules'),
    ('79dcfcfe-1769-47de-9217-5d3dc9e3ff59', 'KiComLib Q', 'KiComLib_Q', 'KiComLib Transistors'),
    ('26edc427-4b22-453c-8a48-d8d2ee808b71', 'KiComLib Power', 'KiComLib_Power', 'KiComLib Power'),
    ('ab4e3f9b-4f60-40ef-b3ae-f0fb4fd7e27f', 'KiComLib Y', 'KiComLib_Y', 'KiComLib Crystals'),
    ('e93a5101-22c8-4ddc-babd-0e5dadbf7a57', 'KiComLib T', 'KiComLib_T', 'KiComLib Transformers'),
    ('75ad0b46-e3f0-4bea-9511-56684e0a32e5', 'KiComLib F', 'KiComLib_F', 'KiComLib Fuses'),
    ('79891de2-1717-43f3-a3f2-e30e3c4166c1', 'KiComLib K', 'KiComLib_K', 'KiComLib Relays');
