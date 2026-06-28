/* Shared org + people dataset for the three "Organisation & medarbejdere" model prototypes.
   Classic script — assigns window.OrgData. Each prototype deep-copies the raw arrays into
   its own state, so editing in one prototype never leaks into another. */
(function () {
  // units: id, type, name, parentId, leaderIds[]  (leaderIds reference people placed IN that unit)
  var units = [
    { id: 'm1', type: 'ministeromrade', name: 'Skatteministeriet', parentId: null, leaderIds: [] },
    { id: 'o1', type: 'organisation', name: 'Skattestyrelsen', parentId: 'm1', leaderIds: [] },
    { id: 'd1', type: 'direktion', name: 'Direktion', parentId: 'o1', leaderIds: ['lars'] },
    { id: 'a1', type: 'omrade', name: 'Borgerområdet', parentId: 'd1', leaderIds: ['mette'] },
    { id: 'k1', type: 'kontor', name: 'Vejledning', parentId: 'a1', leaderIds: ['jens', 'trine'] },
    { id: 't1', type: 'team', name: 'Vejledning Øst', parentId: 'k1', leaderIds: ['camilla'] },
    { id: 't2', type: 'team', name: 'Vejledning Vest', parentId: 'k1', leaderIds: ['henrik'] },
    { id: 'k2', type: 'kontor', name: 'Folkeregister', parentId: 'a1', leaderIds: ['bjorn'] },
    { id: 'a2', type: 'omrade', name: 'Erhvervsområdet', parentId: 'd1', leaderIds: ['kasper'] },
    { id: 'k3', type: 'kontor', name: 'Selskabsskat', parentId: 'a2', leaderIds: ['nadia'] },
    { id: 't3', type: 'team', name: 'Kontrol', parentId: 'k3', leaderIds: [] },
    { id: 'o2', type: 'organisation', name: 'Toldstyrelsen', parentId: 'm1', leaderIds: [] },
    { id: 'd2', type: 'direktion', name: 'Direktion', parentId: 'o2', leaderIds: ['erik'] },
    { id: 'a3', type: 'omrade', name: 'Toldkontrol', parentId: 'd2', leaderIds: ['helle'] },
    { id: 'k4', type: 'kontor', name: 'Grænsekontrol', parentId: 'a3', leaderIds: ['tomas'] },
    { id: 'o3', type: 'organisation', name: 'Gældsstyrelsen', parentId: 'm1', leaderIds: [] },
    { id: 'd3', type: 'direktion', name: 'Direktion', parentId: 'o3', leaderIds: ['rikke'] },
    { id: 'a6', type: 'omrade', name: 'Inddrivelse', parentId: 'd3', leaderIds: ['birgitte'] },
    { id: 'k13', type: 'kontor', name: 'Privat gæld', parentId: 'a6', leaderIds: ['jan'] }
  ];

  // people: id, name, title, email, unitId, leaderId (primary leader; null = apex/øverste leder)
  var users = [
    // Direktion
    { id: 'lars', name: 'Lars Mogensen', title: 'Direktør', email: 'lmo@sktst.dk', unitId: 'd1', leaderId: null },
    { id: 'asger', name: 'Asger Holm', title: 'Chefkonsulent', email: 'aho@sktst.dk', unitId: 'd1', leaderId: 'lars' },
    // Borgerområdet
    { id: 'mette', name: 'Mette Sørensen', title: 'Områdedirektør', email: 'mso@sktst.dk', unitId: 'a1', leaderId: 'lars' },
    { id: 'anton', name: 'Anton Lykke', title: 'Chefkonsulent', email: 'aly@sktst.dk', unitId: 'a1', leaderId: 'mette' },
    // Vejledning (two aligned leaders: jens + trine)
    { id: 'jens', name: 'Jens Kofoed', title: 'Kontorchef', email: 'jko@sktst.dk', unitId: 'k1', leaderId: 'mette', vikar: { leaderId: 'trine', from: '2026-06-23', to: '2026-07-03' } },
    { id: 'trine', name: 'Trine Bjerg', title: 'Kontorchef', email: 'tbj@sktst.dk', unitId: 'k1', leaderId: 'mette' },
    { id: 'liva', name: 'Liva Sand', title: 'AC-fuldmægtig', email: 'lsa@sktst.dk', unitId: 'k1', leaderId: 'jens' },
    { id: 'math', name: 'Mathilde Rask', title: 'Specialkonsulent', email: 'mra@sktst.dk', unitId: 'k1', leaderId: 'jens' },
    { id: 'oscar', name: 'Oscar Lind', title: 'Jurist', email: 'oli@sktst.dk', unitId: 'k1', leaderId: 'trine' },
    // Vejledning Øst (camilla; her leader jens sits one unit up — normal cross-unit leder-of-leder)
    { id: 'camilla', name: 'Camilla Berg', title: 'Teamleder', email: 'cbe@sktst.dk', unitId: 't1', leaderId: 'jens' },
    { id: 'albert', name: 'Albert Friis', title: 'Sagsbehandler', email: 'afr@sktst.dk', unitId: 't1', leaderId: 'camilla' },
    { id: 'emma', name: 'Emma Bro', title: 'Konsulent', email: 'ebr@sktst.dk', unitId: 't1', leaderId: 'camilla' },
    { id: 'victor', name: 'Victor Aagaard', title: 'Sagsbehandler', email: 'vaa@sktst.dk', unitId: 't1', leaderId: 'camilla' },
    // Vejledning Vest (henrik)
    { id: 'henrik', name: 'Henrik Vad', title: 'Teamleder', email: 'hva@sktst.dk', unitId: 't2', leaderId: 'trine' },
    { id: 'sofia', name: 'Sofia Beck', title: 'Fuldmægtig', email: 'sbe@sktst.dk', unitId: 't2', leaderId: 'henrik' },
    { id: 'lucas', name: 'Lucas Holst', title: 'Sagsbehandler', email: 'lho@sktst.dk', unitId: 't2', leaderId: 'henrik' },
    // Folkeregister (bjorn) + one cross-unit exception (carl → mette, who sits in a1)
    { id: 'bjorn', name: 'Bjørn Engel', title: 'Kontorchef', email: 'ben@sktst.dk', unitId: 'k2', leaderId: 'mette' },
    { id: 'august', name: 'August Bang', title: 'Sagsbehandler', email: 'aba@sktst.dk', unitId: 'k2', leaderId: 'bjorn' },
    { id: 'agnes', name: 'Agnes Vedel', title: 'Fuldmægtig', email: 'ave@sktst.dk', unitId: 'k2', leaderId: 'bjorn' },
    { id: 'carl', name: 'Carl Storm', title: 'Sagsbehandler', email: 'cst@sktst.dk', unitId: 'k2', leaderId: 'mette' },
    // Erhvervsområdet (kasper)
    { id: 'kasper', name: 'Kasper Lund', title: 'Områdedirektør', email: 'klu@sktst.dk', unitId: 'a2', leaderId: 'lars' },
    { id: 'nadia', name: 'Nadia El-Amin', title: 'Kontorchef', email: 'nel@sktst.dk', unitId: 'k3', leaderId: 'kasper' },
    { id: 'felix', name: 'Felix Holm', title: 'Chefkonsulent', email: 'fho@sktst.dk', unitId: 'k3', leaderId: 'nadia' },
    { id: 'nora', name: 'Nora Bjørn', title: 'Jurist', email: 'nbj@sktst.dk', unitId: 'k3', leaderId: 'nadia' },
    // Kontrol (leaderless team; pia & magnus report up to nadia in parent unit)
    { id: 'pia', name: 'Pia Krogh', title: 'Specialkonsulent', email: 'pkr@sktst.dk', unitId: 't3', leaderId: 'nadia' },
    { id: 'magnus', name: 'Magnus Dahl', title: 'Fuldmægtig', email: 'mda@sktst.dk', unitId: 't3', leaderId: 'nadia' },
    // Toldstyrelsen
    { id: 'erik', name: 'Erik Storm', title: 'Direktør', email: 'est@toldst.dk', unitId: 'd2', leaderId: null },
    { id: 'helle', name: 'Helle Vinter', title: 'Områdedirektør', email: 'hvi@toldst.dk', unitId: 'a3', leaderId: 'erik' },
    { id: 'tomas', name: 'Tomas Bak', title: 'Kontorchef', email: 'tba@toldst.dk', unitId: 'k4', leaderId: 'helle' },
    { id: 'sigrid', name: 'Sigrid Falk', title: 'Sagsbehandler', email: 'sfa@toldst.dk', unitId: 'k4', leaderId: 'tomas' },
    { id: 'malthe', name: 'Malthe Riis', title: 'Toldekspedient', email: 'mri@toldst.dk', unitId: 'k4', leaderId: 'tomas' },
    // Gældsstyrelsen
    { id: 'rikke', name: 'Rikke Holst', title: 'Direktør', email: 'rho@gaeldst.dk', unitId: 'd3', leaderId: null },
    { id: 'birgitte', name: 'Birgitte Rask', title: 'Områdedirektør', email: 'bra@gaeldst.dk', unitId: 'a6', leaderId: 'rikke' },
    { id: 'jan', name: 'Jan Friis', title: 'Kontorchef', email: 'jfr@gaeldst.dk', unitId: 'k13', leaderId: 'birgitte' },
    { id: 'lene', name: 'Lene Skou', title: 'Fuldmægtig', email: 'lsk@gaeldst.dk', unitId: 'k13', leaderId: 'jan' }
  ];

  window.OrgData = {
    rawUnits: units,
    rawUsers: users,
    LABEL: { ministeromrade: 'Ministerområde', organisation: 'Organisation', direktion: 'Direktion', omrade: 'Område', kontor: 'Kontor', team: 'Team', enhed: 'Enhed' },
    SHORT: { ministeromrade: 'Min.', organisation: 'Org.', direktion: 'Dir.', omrade: 'Område', kontor: 'Kontor', team: 'Team', enhed: 'Enhed' },
    CHILD: { ministeromrade: 'organisation', organisation: 'direktion', direktion: 'omrade', omrade: 'kontor', kontor: 'team', team: 'enhed', enhed: null },
    ACCENT: { ministeromrade: '#55565a', organisation: '#066b43', direktion: '#1a6a86', omrade: '#0f766e', kontor: '#8a6a00', team: '#5a6b86', enhed: '#86705a' },
    TINT: { ministeromrade: '#ececed', organisation: '#e1efe9', direktion: '#e3eef2', omrade: '#e2efed', kontor: '#f4eed8', team: '#eaedf3', enhed: '#f2ece6' },
    ORD: { ministeromrade: -1, organisation: 0, direktion: 1, omrade: 2, kontor: 3, team: 4, enhed: 5 },
    clone: function () {
      return {
        units: JSON.parse(JSON.stringify(units)),
        users: JSON.parse(JSON.stringify(users))
      };
    }
  };
})();
