--
-- KiCADDbL default seeder script
--

-- Fills the database with default data


--------------------------------------------------------------------------------------------------
-- ADD DISTRIBUTORS
--------------------------------------------------------------------------------------------------

INSERT INTO public.distributor (uuid, name, url, trustedparts_id) VALUES
    ('54abc08f-bf78-47cc-96e8-153341dc8a7e', 'Mouser Electronics', 'https://www.mouser.de/', '6'),
    ('60c71433-fe81-45fe-a488-c3ccba9e7952', 'RS Components', 'https://www.rs-online.com/', '1388'),
    ('baec89c7-6785-4bf0-9d3b-6d8e5bf24f5d', 'Arrow Electronics', 'https://www.arrow.com/', '2'),
    ('93c84acb-cfdb-4acb-9fac-ed6bbbed8b0f', 'DigiKey', 'https://www.digikey.de/', '4'),
    ('48b93f89-c567-474c-9e2c-38da01811fff', 'Farnell', 'https://www.farnell.com/', '3874'),
    ('b67e178f-2391-4393-9fc2-c8eeacf4a365', 'TME', 'https://www.tme.eu/', '6287');



--------------------------------------------------------------------------------------------------
-- ADD MANUFACTURERS
--------------------------------------------------------------------------------------------------

INSERT INTO public.manufacturer (uuid, name, url, trustedparts_id) VALUES
    ('0bb7a9ed-9b18-43d9-8a3d-936c314a5251', 'Texas Instruments', 'https://www.ti.com/', '205'),
    ('78498fe6-4c3e-475a-9a59-3ed79f38ad30', 'Vishay', 'https://www.vishay.com/', '211'),
    ('cad7724d-c569-42d4-bb41-6e12c30b99a6', 'onsemi', 'https://www.onsemi.com/', '176'),
    ('77a52c69-c6de-4789-a197-0e1128e8afbb', 'Würth Elektronik', 'https://www.we-online.com/', '536'),
    ('31202943-fc37-4946-a6e8-672b565bd309', 'Amphenol', 'https://amphenol.com/', '102'),
    ('cf4d6dde-6568-45f1-a114-a1aa384fcca2', 'Analog Devices', 'https://www.analog.com/', '103'),
    ('c944d054-c5cc-4802-9d8a-39353dbd0216', 'Samsung Semiconductor', 'https://semiconductor.samsung.com/', '1729'),
    ('d74339a8-158d-4cb6-89ef-1b7b1f4926db', 'Intel', 'https://www.intel.com/', '147'),
    ('a7c181b8-8b2b-4020-b371-906c077302d6', 'AMD', 'https://www.amd.com/', '220'),
    ('a878cb7c-1325-41bf-96a7-740f5ce3f85e', 'Murata Manufacturing', 'https://www.murata.com', '224'),
    ('b08cc103-bc05-4ed3-adf2-7fafa24e62a1', 'KEMET', 'https://www.kemet.com', '152'),
    ('bae6ef42-eb45-4f10-9128-db4d52bc9c98', 'Panasonic', 'https://industrial.panasonic.com/', '319'),
    ('6431e170-281a-4947-a679-59a6b2b005c0', 'TDK Electronics', 'https://www.tdk-electronics.tdk.com/', '130'),
    ('6aac2925-8c93-43ba-9029-1ca28394cb1e', 'Microchip Technology', 'https://www.microchip.com/', '165'),
    ('ca9a4f96-72b5-453f-84b7-c8100d02bc14', 'Diodes Incorporated', 'https://www.diodes.com', '529'),
    ('adaf3fda-2e38-4478-b115-ad1e259ee48d', 'Infineon Technologies', 'https://www.infineon.com/', '146'),
    ('ecca1bbb-5f81-416e-a294-cb7405cf450a', 'Harting', 'https://www.harting.com/', '140'),
    ('81c70337-d1df-4393-af99-5937d5e5c9f2', 'Kyocera AVX', 'https://www.kyocera-avx.com/', '106'),
    ('aa4dcead-abd1-4c73-affe-2b4462178edd', 'Maxim Integrated', 'https://www.maximintegrated.com/', '103'),
    ('85949a64-dec2-4c3b-ab3d-6c542e5ecb0d', 'Ohmite', 'https://www.ohmite.com/', '174'),
    ('84f684a6-fe27-4a11-9864-7482fd4aede9', 'TE Connectivity', 'https://www.te.com/', '204'),
    ('5cd6dde3-304e-4a97-9c72-f1753e4be807', 'Molex', 'https://www.molex.com/', '166'),
    ('f0e6b579-85b4-41d3-bbd2-ded5e13b85a4', 'Littelfuse', 'https://www.littelfuse.com/', '160'),
    ('3fe7074c-c8fd-4e9f-a471-7d16d646e099', 'Bourns', 'https://www.bourns.com/', '109'),
    ('b90a6b57-aa7f-4d14-8a90-7fde5a4f0384', 'Samtec', 'https://www.samtec.com/', '900'),
    ('1bd2cb07-be9f-4611-b4ce-9f9b46430b8a', 'NXP Semiconductors', 'https://www.nxp.com/', '173'),
    ('b808455b-d98e-45eb-841a-897d4edd5242', 'ADI Trinamic', 'https://www.analog.com/', '1651'),
    ('28b657a4-a7fc-4165-8b72-b8e8d47ab0e3', 'Abracon', 'https://abracon.com/', '264'),
    ('76e4205c-edc4-4a3b-843c-623e89913719', 'Hirose', 'https://www.hirose.com/', '141'),
    ('110108a7-5c0a-4a17-987f-d7c9fa4037bd', 'Phoenix Contact', 'https://www.phoenixcontact.com/', '182'),
    ('e76f89be-8be4-48d9-9a76-b9ff5200eaa1', 'STMicroelectronics', 'https://www.st.com/', '379'),
    ('293aabdb-0502-473a-b091-8941d83817c4', 'JST', 'https://jst.de/', '705'),
    ('a86aa586-2788-4015-af27-dbdefef223fe', 'JAE', 'https://www.jae.com/', '691'),
    ('f8f25838-0341-499c-8adf-c4c5ef32a47c', 'MCC', 'https://www.mccsemi.com/', '164'),
    ('3b3eea65-667c-4d50-88f5-ab735f63db44', 'Global Connector Technology', 'https://gct.co/', '1166'),
    ('f142a6f0-b82d-433c-9b2b-62b6829d15ea', 'Monolithic Power Systems', 'https://www.monolithicpower.com/', '1687'),
    ('db10f915-a566-4c52-8754-5b9b8b5ee3d8', 'ECS International', 'https://ecsxtal.com/', '553'),
    ('0c9920a2-0814-41fe-b373-a9a7a323a13d', 'Advanced Thermal Solutions', 'https://www.qats.com/', '281'),
    ('77c6b13d-69fa-4503-b89e-6e8e99458dd1', 'Pulse', 'https://www.pulseelectronics.com/', '805'),
    ('d00823b6-cfa7-4803-8402-735cbddeb6fd', 'Coilcraft', 'https://www.coilcraft.com/', '1877'),
    ('dceb9095-1636-4de6-b58e-c04c41551f54', 'Bel Fuse', 'https://www.belfuse.com/', '3574'),
    ('8b132ad8-fe5d-4d49-a410-32d70f28fece', 'Nexperia', 'https://www.nexperia.com/', '3593'),
    ('1ac667be-f2c3-4695-bb64-e1b90c31beac', 'I-PEX', 'https://www.i-pex.com/', '5597'),
    ('307d8249-086a-471a-ad95-1621a1e90d77', 'Keystone Electronics', 'https://www.keyelco.com/', '153'),
    ('0dcfe185-c7b7-48a7-8a86-e51b77483dff', 'Susumu', 'https://www.susumu.co.jp/', '704'),
    ('e8debed2-1af9-4abe-89dd-6465e764440b', 'Chemi-Con', 'https://chemi-con.com/', '658'),
    ('4a691abf-657e-47cb-b83c-cc2241cee305', 'Same Sky', 'https://www.sameskydevices.com/', '4901'),
    ('05564ffb-f3b0-4b4c-887f-ef6c20da9369', 'YAGEO', 'https://www.yageogroup.com/', '533'),
    ('8db4c235-27db-43f4-9a76-5e4afce1581b', 'Toshiba', 'https://toshiba.semicon-storage.com/', '207'),
    ('ba1cab0e-c692-4776-b9d8-237dce59dbe7', 'Aces Connectors', 'https://www.acesconn.com/', '6578'),
    ('4c00d812-1138-4584-a5f9-40f2d65e8c6b', 'ROHM Semiconductor', 'https://www.rohm.com/', '192'),
    ('703d9471-3f6f-4d1a-9342-35c8cbb14f3c', 'Lattice Semiconductor', 'https://www.latticesemi.com/', '158'),
    ('c0d844f8-b7ab-4076-8516-dccd661affe2', 'Metz Connect', 'https://www.metz-connect.com/', '1162'),
    ('81c371cf-2c0e-4459-ba2d-34d0e712dc91', 'Silvertel', 'https://silvertel.com/', '1575'),
    ('116c78f4-43e3-4279-8ad5-61c86412b7e7', 'PEAK Systems', 'https://www.peak-system.com/', NULL),
    ('56c5452c-813e-4f6b-9fd3-206ac30f1fa1', 'ISSI', 'https://www.issi.com/', '689'),
    ('814b1ad9-7db8-4cf6-9e67-2a7956d73edd', 'Semtech', 'https://www.semtech.com/', '243'),
    ('ca039152-a2ec-4064-a200-8ecf3144c49b', 'Walsin', 'https://www.passivecomponent.com/', '568'),
    ('c366f5e5-41f0-4b9c-95f8-1f11d9226aec', 'Harwin', 'https://www.harwin.com/', '664'),
    ('5c151780-514c-45bc-8707-4183e47d00c0', 'Broadcom Limited', 'https://www.broadcom.com/', '1451'),
    ('20e5373d-1d2c-4cbd-8550-4246c0de0ca3', 'Adam Technologies', 'https://www.adam-tech.com/', '453'),
    ('62a85bb9-c903-4c26-b551-e364120e5cb6', 'Zettler', 'https://zettlerelectronics.com/', '1030'),
    ('6c9aca90-e7ba-4a83-a366-043577f81c64', 'Diotec', 'https://diotec.com/', '2503'),
    ('16f63cd6-ea07-4564-bcad-f6b8f578122f', 'KOA Speer', 'https://www.koaspeer.com/', '155');
