# Ideias (não implementadas)

Referências de como o Dubs Mint Menus organizava (só para entender; não copiamos): `_dev/_decompiled/DubsMintMenus_1.7`.

1. **Filtros de armazenamento** (stockpiles/prateleiras): busca + presets salvos de `ThingFilter` ("só munição 5.56", "só comida crua"). Dubs mexia em `Listing_TreeThingFilter`.
2. **Construção**: lista agrupada de edifícios e materiais (o Mounted Arms já tem a aba "Militar").
3. **Pesquisa**: lista/busca/fila com custo e o que cada projeto libera (Dubs: `MainTabWindow_MintResearch`).
4. **Plantio/zonas**: seletor de cultura com rendimento, fertilidade e estações (Dubs: `Dialog_FancyDanPlantSetterBob`).
5. **Equipamento/roupa dos colonos**, políticas de roupa e de drogas por "sets".
6. **Produção travada**: na janela "Tarefas", mostrar por que cada bancada está parada (falta material, habilidade, energia, pesquisa).
7. **Recursos fixados**: clicar no recurso abre a bancada mais livre filtrada por ele; agrupar fixados em seções nomeadas; "fazer o máximo" direto do painel; descontar a fila já reservada no "dá para fazer".
8. **Janela de cirurgia** (pedido do usuário, 15/nov, depois do hotfix 1.3.1): na aba Operações, uma linha por cirurgia + parte ("Instalar perna de pau (perna esquerda)"), opções geradas como o vanilla (`GetPartsToApplyOn`, `AvailableOnNow(pawn, part)`, `AvailableReport`), grupos por tipo/parte do corpo, filtros (com materiais, disponíveis), tarefa criada só por `HealthCardUtility.CreateSurgeryBill`, sem opções de repetição. Hoje mostraria 106 linhas sem organização.
