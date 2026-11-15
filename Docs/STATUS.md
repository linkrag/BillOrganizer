# Status

Compila em 1.6 e 1.7 (3/nov/2026). **Nada foi testado no jogo ainda**; o visual em especial só se valida com screenshot do usuário.

## Versão e changelog (13/nov/2026) - versão atual **1.3.0**, sem efeito no jogo além do número
- `<modVersion>1.3.0</modVersion>` no `About/About.xml` (o campo existe no `ModMetaData` da 1.6 e da 1.7; o menu de mods mostra a versão).
- `Docs/CHANGELOG.md` (novo, inglês, para jogador): uma versão por dia com mudança visível desde a publicação (1.0.0 = 7/nov). Montado por workflow de agentes a partir de STATUS/HISTORY/git e conferido item a item por um revisor cético.
- `Docs/WORKSHOP_DESCRIPTION.txt`: linha "Mod version" ao lado de "Versions" e seção [h2]Changelog[/h2] no fim (a 1.0.0 vira uma linha só, porque os recursos já estão descritos acima; o limite da Steam é 8000 caracteres). **Colar na página da Oficina: pendente com o usuário.**
- Nada testado no jogo (só o número no menu de mods mudaria).

## Clique no recurso seleciona e foca a pilha (13/nov/2026) - instalado (1.6 e 1.7), **NÃO testado no jogo**
- Pedido do usuário: clicar num recurso da lista à esquerda (`ResourceList`), do painel de fixados (`ResourcePanel`) ou do readout vanilla (quando a nossa lista está desligada) seleciona a pilha no mapa e centraliza a câmera (`ResourceJump` em `ResourcePanel.cs`, `CameraJumper.TryJumpAndSelect`).
- Cada clique no mesmo recurso vai para a próxima pilha (a maior primeiro, depois volta ao começo); Shift+clique seleciona todas e foca a maior. As pilhas são as que o contador conta (guardadas em estoque, fora do fog); sem nenhuma, qualquer uma no mapa; sem nada, mensagem `BO_ResJumpNone`. Ctrl+clique continua fixando (agora também no painel) e o botão direito continua no menu. Tooltip `BO_PinRowTip` com as dicas (EN/PT).
- Teste: clicar em Aço (vai à maior pilha), de novo (próxima), Shift+clique (todas), o mesmo no painel, Ctrl+clique e botão direito sem mudança, recurso ausente mostra a mensagem.

## "Até X" anda pelo que cada construção produz (13/nov/2026) - **confirmado em jogo pelo usuário (13/nov, todos os testes)**
- **Rodada 3 (auditoria por workflow de 3 agentes: nosso código, jogo/Dubs descompilados, defs dos 191 mods ativos)**: 1.144 de 2.569 receitas com produto tinham passo diferente do lote (CE 930; VCE 29, aoba 21, VGE 20, VBE 38, vanilla 15+3 DLC, MA/MAS 16...; lista completa no scratchpad da sessão, `mismatches.csv`). O passo já estava coberto pelo `TargetStepFix`; o que sobrava fora do lote era o **valor** da meta, agora corrigido:
  1. "+" da linha da receita (aba e janela) criava a tarefa com o `cfgQty` da receita anterior (o detalhe ainda não tinha recalculado): `AddOptions.qtyAuto` + `Configure` usa 1 lote desta receita quando o valor é nosso e nunca fica abaixo de 1 lote (`RecipeEntry.SetTarget`).
  2. Chip de modo do cartão (`BillCard.CycleMode`) mantinha o 10 vanilla: agora arredonda para cima em lotes inteiros (`RecipeEntry.SnapTarget`).
  3. Toda tarefa nova (vanilla, Nice, Dubs, nossa: todas passam por `BillUtility.MakeNewBill`) nascia com meta 10: postfix `Patch_MakeNewBill_TargetStep` põe em lotes inteiros (200 no x200, 12 no x3; 10 fica no x5).
  4. Tarefas já salvas: `TargetStepFix.NormalizeBills` (no `Game.FinalizeInit`) arredonda para cima as metas fora do lote em todos os mapas e loga quantas mudou; "retomar quando tiver" anda junto.
  5. "Máx" em "Até X" arredonda para baixo em lotes (mesmas construções, nunca 1 lote além do estoque); o valor herdado de outra receita (`ApplyBatchTarget`) arredonda para cima.
  - Tudo depende da opção `stepByBatch`. Mounted Arms (Stinger 4) e Strategic (15 mísseis, 1) ganharam `targetCountAdjustment` no XML.
  - Não mexido (decisão): o -/+ vanilla não "encaixa" valor fora do lote (com a normalização todos ficam no lote); o "retomar quando tiver" do diálogo vanilla (meta - 1) é vanilla. Achado não verificado e fora do escopo: "Até X" oferecido para receita que o jogo não sabe contar (sem produto/abate).
  - Teste: carregar o save e ver no `Player.log` "'Until X': N bill target(s) put on whole batches"; tarefa nova de munição x200 trocada para "Até X" mostra 200; "+" da linha logo após outra receita cria com 1 lote da receita certa; chip de modo do cartão x3 vai a 12.
- **Depois (pedido do usuário: "valer para todos os itens")**: `TargetStepFix.cs` reescreve o `targetCountAdjustment` de **toda** receita com produto para a contagem do produto, ao iniciar/carregar o jogo (postfix em `Game.FinalizeInit`, quando já existem as receitas que o CE gera no código) e ao fechar as opções. Assim o -/+ da linha de tarefa vanilla (`Bill_Production.DoConfigInterface`, único leitor do campo no core 1.7 descompilado), o Nice Bill Tab e outros mods também andam pelo lote. Opção `stepByBatch` (ligada; seção Bancadas, chaves `BO_SetStepByBatch*` EN/PT); desligar restaura os valores originais guardados. Modo dev loga quantas receitas mudaram. Receita sem produto (abate, cirurgia...) não muda. **Não testado no jogo**; teste extra: o -/+ vanilla/Nice da tarefa 81mm anda de 5; desligar a opção volta a 20.
- Relato do usuário (print): "Make 81mm (EMP) mortar shells x5" abria "Até X" em 20 e os presets eram 20/100/500/1000, mas cada construção faz 5.
- Causa (verificada no XML do CE): `AmmoRecipeBase` põe `targetCountAdjustment = 20` em toda receita de munição; `RecipeEntry.TargetStepOf` usava o maior entre isso e a contagem do produto. Nas balas x200 vencia o 200 (por isso o caso de 5/nov funcionou); no x5 vencia o 20.
- Correção: o passo é a contagem do 1º produto (`products[0].count`); `targetCountAdjustment` só vale para receita sem produto. Vale para o detalhe (valor inicial, -/+, presets 5/25/125/250) e o +/- do cartão da fila. Balas x200 continuam em 200.
- Teste: 81mm x5 em "Até X" deve abrir em 5 com presets 5/25/125/250; +/- do cartão anda de 5; uma munição x200 continua em 200.

## Força-tarefa: prioridade de trabalho temporária (13/nov/2026) - instalado (1.6 e 1.7), **confirmado em jogo pelo usuário (13/nov, todos os testes)**
**Confirmado (prints)**:
- botão "Task force (1)" ao lado de "Manual priorities" e status "Craft: 26 colonists, 2d left" + "End" à direita, sem sobrepor;
- diálogo com prioridade atual por trabalho, habilidade e resumo;
- início com 1 trabalho (Craft) e com 3 (Basic, Construct, Craft): os 26 colonos em 1, inclusive quem estava desligado;
- "End" antes do tempo: cada colono voltou exatamente ao valor anterior (4, 2, 3, 1 e desligado conferidos célula a célula).

**Depois (13/nov, relato do usuário: "testes feitos, tudo OK")**: mudança manual no meio, fim natural com a mensagem, salvar e carregar, "Interromper" e `Player.log` também confirmados. Ideias de visual oferecidas ao usuário: checkbox sem X vermelho na lista de trabalhos; uma coluna de habilidade por trabalho (hoje é só a do 1º trabalho que tem habilidade).
Pedido do usuário: depois de uma invasão, ~100-150 animais fugiram do curral pela cerca quebrada e só 3-4 colonos tinham Lidar com animais em 1. Ele quer um mutirão por um tempo (1 h, 1 dia) que volte sozinho.
- **Verificado (IL 1.7)**: `Pawn_WorkSettings.SetPriority` loga erro em trabalho desativado (por isso só mexemos em `!WorkTypeIsDisabled`). `GetPriority` devolve 3 para qualquer valor > 0 com prioridades manuais desligadas. A aba Trabalho desenha o checkbox em (5, 5, 140) e os rótulos em x 370 e 630 (`ExtraTopSpace` 40).
- **`Source/TaskForce.cs`** (novo):
  - `TaskForceManager` (GameComponent salvo) com forças-tarefa `{trabalhos, início, fim, membros}`; cada membro é (colono, trabalho, original, posto);
  - `Start`: guarda o original e põe a prioridade (padrão 1). Par que já está numa força-tarefa passa para a nova e mantém o original de antes da 1ª. Com prioridades manuais desligadas, só liga o trabalho de quem estava com 0;
  - fim a cada 250 ticks: restaura **só se o colono ainda tem o valor posto** (mudança manual vence) e mostra a mensagem "N colonos voltaram... / N mantiveram o que você mudou";
  - "Interromper o trabalho atual": encerra o job só se é trabalho comum (work giver, não forçado, não convocado) que a nova prioridade vence;
  - `Patch_WorkTab`: botão "Força-tarefa (N)" no espaço livre do topo da aba Trabalho, tooltip com as ativas e os valores de volta, e status + "Encerrar" à direita quando a janela é larga.
- **`Source/Dialog_TaskForce.cs`** (novo): trabalhos (vários), prioridade 1-4, duração (1h/3h/6h/12h/1d/2d + slider até 72 h), interromper, aviso + "Ligar prioridades manuais", colonos (capazes marcados, prioridade atual por trabalho, habilidade relevante, Todos/Nenhum), ativas com "Encerrar agora" e um chip por colono que tira só ele. As últimas escolhas ficam nas opções (`tfWorks/tfHours/tfPrio/tfInterrupt`). Pausa o jogo enquanto aberto.
- **Revisão por workflow de agentes** (3 revisores: lógica, UI, API; cada achado verificado por um cético). Corrigidos 5 confirmados:
  1. Com prioridades manuais desligadas no meio, a restauração podia pular o `SetPriority` (o `GetPriority` mascara o valor cru) e deixar o 1 gravado. Agora sempre grava, no início e no fim;
  2. "Interromper" cortava resgate, tratamento, cirurgia, paciente na cama e incêndio. Agora nunca interrompe Médico/Paciente/Bombeiro, work giver de emergência, job não `casualInterruptible` nem quem carrega pawn/corpo, e só conta os trabalhos que o colono pode fazer;
  3. A mensagem "mantiveram o que você mudou" contava quem ficou incapaz. Agora o resultado tem 3 estados e o incapaz fica fora;
  4. A duração de 25-71 h aparecia arredondada ("1.3 dias"). Agora mostra dias + horas exatos;
  5. O aviso de prioridades manuais tinha altura fixa e cobria o checkbox. Agora a altura é medida.
  Descartados na verificação: vazamento de fonte e tooltip duplicada.
- Opção do mod "Botão Força-tarefa na aba Trabalho" (seção Trabalho, `taskForce`, padrão ligada). Chaves `BO_SecWork`, `BO_SetTaskForce(Tip)` e `BO_TF*` (EN e PT-BR).
- Descrição da Oficina (`Docs/WORKSHOP_DESCRIPTION.txt`) revisada por inteiro, com os recursos que faltavam desde 11/nov, e `<description>` do `About.xml` atualizada. **Pendente com o usuário**: colar a descrição na página da Oficina.
- **A testar**:
  1. Aba Trabalho: o botão aparece entre "Prioridades manuais" e "<= Maior prioridade", sem cobrir nada (também com o Numbers ativo).
  2. Força-tarefa de Lidar com animais, 1 h, todos: os números viram 1 na aba, quem fazia trabalho comum troca na hora e os colonos laçam os animais de volta ao curral.
  3. Num colono, mudar à mão para 2 no meio. Ao fim: esse fica em 2, os outros voltam ao original (inclusive desligado) e aparece a mensagem.
  4. Salvar e carregar no meio: o tempo continua e a restauração acontece.
  5. "Encerrar" (aba), "Encerrar agora" (diálogo) e o chip de um colono restauram na hora.
  6. 2ª força-tarefa sobre a 1ª: ao fim, volta ao valor de antes da 1ª.
  7. Prioridades manuais desligadas: aviso e checkbox que liga.
  8. Sem erro no `Player.log`.

## Abate automático: proteger bebês (13/nov/2026) - instalado (1.6 e 1.7); **confirmado em jogo pelo usuário (13/nov: visual com 2 prints + testes de abate, tudo OK)**
- **Confirmado (prints)**: a coluna "Bebês" (Atual + Slaughter) aparece no fim, sem sobrepor "Vinculados"; marcar Coyote (30 bebês) devolve os bebês às colunas de jovens (machos jovens 0 -> 20, em vermelho acima do máx. 10).
- **Total menor que a soma das colunas não é nosso**: o Zoology (`com.abobashark.zoologymod`, ativo) tem um postfix em `Dialog_AutoSlaughter.RecalculateAnimals` que tira as fêmeas lactantes de fêmeas/fêmeas jovens/total, inclusive as grávidas que o vanilla já não contava (por isso o total cai, até 0), e depois da ordenação (a lista fica fora de ordem). Verificado no descompilado do Zoology. Com Coyote liberado, 8 + 20 + 10 = 38, mas aparece 28 (10 lactantes). Só afeta o número mostrado: o `AnimalsToSlaughter` não muda (o Zoology bloqueia a lactante no `WorkGiver_Slaughter`).
- **Confirmado (13/nov, relato do usuário)**: bebês protegidos sem designação de abate e, liberados, abatidos conforme os limites.
Pedido do usuário: na janela vanilla de abate automático, poder não abater bebês (filhote de cavalo, leitão...), se o bebê render menos carne.
- **Verificado (XML 1.7)**: `MeatAmount` tem `StatPart_BodySize`; `AnimalBaby` tem bodySizeFactor 0.2 (`AnimalBabyTiny` 0.1, juvenil 0.5). O bebê rende ~20% da carne do adulto (10% no minúsculo).
- **Vanilla (IL 1.7)**: `AutoSlaughterManager.AnimalsToSlaughter` filtra por `CanAutoSlaughterNow`, conta "jovem" = `!CurLifeStage.reproductive` (juvenil + bebê) e abate do mais velho para o mais novo; o cache só suja em spawn/despawn/config.
- **`Source/AutoSlaughterBabies.cs`** (novo):
  - bebê = 1º de vários estágios de vida e não reprodutivo (`IsBaby`; animais não têm `developmentalStage`);
  - `MapComponent_SlaughterBabies` (salvo no mapa): animais com bebês liberados, **padrão nenhum** (bebês protegidos); a cada 2500 ticks suja o cache vanilla para o bebê que cresceu entrar na conta;
  - transpiler no getter `AnimalsToSlaughter` troca a chamada de `CanAutoSlaughterNow` pela nossa (bebê protegido fica de fora, como o vinculado não permitido: não é abatido **nem conta** nos limites). Se não achar exatamente 1 chamada, avisa no log e fica vanilla;
  - janela: postfix em `CountPlayerAnimals` (tira os bebês protegidos de jovens/total), coluna "Bebês" (Atual + Permitir abate) no fim da linha (`InitialSize` +140, `CalculateLabelWidth` -140, `DoAnimalHeader`, `DoAnimalRow`). A coluna **sempre** aparece com a opção ligada (é o único jeito de liberar bebês): em tela estreita (escala de UI alta) a janela cresce o que der e o resto sai da coluna do nome (mín. 40 px). Qualquer exceção na janela desliga a coluna **e a proteção** (volta às regras vanilla, com aviso no log), para o bebê nunca ficar protegido sem o checkbox.
- **Revisão por workflow de agentes** (2 revisores + verificação adversarial): 1 achado confirmado e corrigido (a 1ª versão escondia a coluna em tela < 1230 px de UI, mas mantinha os bebês protegidos, sem o checkbox para liberar). A conta do layout foi conferida contra o vanilla: a faixa de 140 px não sobrepõe as colunas.
- Opção do mod "Abate automático: coluna Bebês" (seção Animais, `slaughterBabies`, padrão ligada). Desligada = regras vanilla.
- Chaves novas `BO_SecAnimals`, `BO_SetSlBabies(Tip)`, `BO_SlBabies(Tip)`, `BO_SlCurrentTip`, `BO_SlAllowTip` (EN e PT-BR); "Atual" e "Permitir abate" usam as chaves do jogo.
- Assinaturas conferidas na referência 1.6 (mesmos métodos e nomes de parâmetro); o corpo do getter em 1.6 não foi visto (o transpiler avisa se mudar).
- **A testar**:
  1. Animais > Abate automático: coluna "Bebês" à direita, sem sobrepor "Vinculados".
  2. Animal com filhotes e máx. machos jovens = 0: bebês não são designados e somem da contagem de jovens; juvenis são abatidos.
  3. Marcar "Permitir abate": bebês voltam a contar e a ser abatidos (modo dev: tooltip do nome lista "Animals to slaughter").
  4. Salvar/carregar mantém a escolha; bebê que vira juvenil entra na conta em até ~2500 ticks.
  5. Desligar a opção do mod: janela e regras vanilla.
  6. `Player.log` sem aviso `[BillOrganizer] AutoSlaughterManager...` nem `'Babies' column turned off`.

## Aba compacta: colar e X próprios (12/nov/2026) - instalado (1.6 e 1.7), **confirmado em jogo pelo usuário (12/nov, 3 prints)**
- Confirmado: cabeçalho "LARGE MACHINING TABLE" com o X (tooltip "Close"), sem X vanilla no canto. Ícone de colar ao lado de "Ações", sem o vanilla por cima. "Copiar fila" com a mensagem "Queue copied (6 bills)". O caso da receita indisponível não foi montado no teste (difícil de reproduzir); os cliques normais na fila seguem abrindo o detalhe.
Pedido do usuário (print): o ícone "colar" vanilla aparecia por cima do "Ações" e o X de fechar ficava atrás do painel de detalhe.
- **Colar** (verificado no IL 1.7): `ITab_Bills.FillTab` desenha o colar na posição fixa `WinSize` (420-48, 3) e só quando `BillUtility.Clipboard` não é nulo. O prefix da aba compacta esvazia o clipboard durante o `FillTab`, e um `Finalizer` devolve no fim (roda mesmo com exceção; se um cartão copiou outra tarefa no meio, essa fica). Tudo nosso lê o clipboard por `Patch_BillTab.Clipboard`, inclusive `Templates.HasQueueClip/TryPasteQueue/CopyQueue`. No lugar do vanilla há um botão de colar nosso ao lado de "Ações" (`QueuePanel.PasteButton`). Ele só aparece quando há algo copiado e fica apagado se a tarefa não serve na bancada ou a lista está cheia. A tooltip diz o que vai colar: a tarefa, ou a fila com "N de M servem" (`BO_QPasteQueueTip`).
- **X** (verificado no IL 1.7): `InspectTabBase.DoTabGUI` chama `Widgets.CloseButtonFor` antes do `FillTab`, então o nosso painel cobria o X. Novo `Patch_TabCloseButton` (prefix em `Widgets.CloseButtonFor`) pula o X vanilla só quando a janela desenhada é a `ImmediateWindow` da aba e a aba aberta é `ITab_Bills` com a aba compacta ativa (`Patch_BillTab.DrawingCompactTab`). Qualquer falha devolve o vanilla. O painel da direita ganhou um cabeçalho com o nome da bancada (em maiúsculas, como o "FILA") e o nosso X (fica vermelho no hover, `BO_CloseTab`), que fecha a aba como o vanilla (`IInspectPane.CloseOpenTab` + som). `RecipeDetail.Draw` ganhou `box = false` para o host desenhar a caixa.
- **Receita indisponível nunca aparece** (pedido do usuário, 12/nov): saiu a entrada "só consulta" da aba compacta (`extra`/`RecipeEntry.ForRecipe`). Na aba e na janela, clicar numa tarefa cuja receita a bancada não oferece agora (pesquisa não feita, mod removido) só mostra uma mensagem (`RecipeEntry.SayUnavailable`, `BO_RecipeUnavailable`). Ninguém consegue abrir nem adicionar essa receita. Continua valendo o casamento pelo produto (`ForBill`): se outra receita disponível faz o mesmo item, é ela que abre.
- **A testar**: copiar um cartão → aparece o ícone colar ao lado de "Ações" e nenhum ícone vanilla por cima. Colar em outra bancada (apagado + tooltip se não serve). Copiar fila → tooltip "N de M". O X do cabeçalho fecha a aba e não sobra X vanilla no canto. Abas de outros objetos continuam com o X vanilla.

## Bônus e efeitos dos itens (11/nov/2026, noite) - **confirmado em jogo (12/nov, 5 prints do usuário)**, correções de 12/nov **confirmadas em jogo**
**Confirmado em jogo (12/nov)**:
- seção do detalhe com supressão de escravo, banda de mecanoides +3 (airwire headset), os 10 bônus do Dreamcatcher (RoM) e o bloco completo da droga (flake);
- marca na lista com tooltip, na aba compacta e na janela; refeições e remédio sem a marca;
- busca `+band`;
- Comparar com as linhas de bônus de dois escudos (mod).

**Não visto ainda**: jump pack/escudo, implantes, gestador, ordenar por coluna de bônus.

**Correções de 12/nov** (compilam em 1.6 e 1.7; deploy feito em 12/nov; **confirmadas em jogo pelo usuário (12/nov, 2 prints)**):
- As colunas de bônus agora saem do que a busca e os filtros deixaram (`StatTable.For(..., bonusFrom)`, `Window_RecipePicker.Refresh`). Antes, `+band` em "Todas" mostrava "Aiming time" e "Move speed" com "-".
- No Comparar, a coluna de nomes tem a largura do maior nome, até 42% do painel. Antes era fixa em 78 px e cortava "Aiming ac...".
- Roupa sem armadura nem isolamento (airwire headset) não mostra mais as 4 linhas zeradas de atributos. Uma faixa como "0 a 3 mm" continua aparecendo.

Texto original da rodada de 11/nov:
Pedido vindo da Oficina: *"Doesn't show extra bonuses that certain items give, like bandwidth limit enhancements for mechanitor items"*. O usuário escolheu todo o escopo (efeitos e lugares).
- **`Source/Bonus.cs`** (novo): lê uma vez por produto (cache) o que o item faz além dos stats de combate:
  - `equippedStatOffsets` (banda de mecanoides, sensibilidade psíquica, velocidade...), no formato do cartão de info;
  - habilidades (`CompProperties_EquippableAbility*`), verbos de roupa (jump pack), escudo (`EnergyShieldEnergyMax`/`RechargeRate`) e o `SpecialDisplayStats` dos comps (cargas, interceptor, comps de outros mods);
  - hediff ao vestir (`CompProperties_CauseHediff_Apparel`, com o 1º estágio);
  - implante (hediff cujo `spawnThingOnRemoved` é o item: eficiência da parte e estágio);
  - mecanoide (`BandwidthCost`, entradas da categoria Mechanoid, habilidades do PawnKind);
  - ingeríveis (nutrição, alegria, categoria, `outcomeDoers`, `DrugStatsUtility`).
  Cada fonte fica num try/catch (com o modo dev, a falha vai para o log). Comps de arma e munição só entram se forem de habilidade (o CE repetiria o pente).
- **Detalhe**: seção "Bônus e efeitos" depois dos atributos, com subtítulos por grupo. Clique numa habilidade ou hediff abre o cartão de info. Valores em branco, só com o sinal.
- **Comparar**: linhas de bônus pela união dos itens fixados, sem verde/vermelho, e uma linha "Concede" com as habilidades.
- **Busca**: prefixo `+` (`+banda`, `+bandwidth`, `+psíquica`); palavra sem prefixo também acha esses textos.
- **Lista**: glifo novo (`UI/bonus.png`, faísca verde-água, `Ui.Bonus`) ao lado do nome, na janela e na aba compacta, com os bônus na tooltip. Nutrição, alegria e textos genéricos de comps são "menores" (`BonusLine.minor`): aparecem no detalhe e na busca, mas não acendem a marca (senão todo o fogão teria a marca).
- **Colunas**: até 2 colunas `bonus:<stat>` para os stats de `equippedStatOffsets` que 2+ receitas do grupo têm (ou 1 se o grupo tem 1 receita), antes das de produção; ordenam pelo cabeçalho. Os rótulos vêm do jogo (`StatInfo.labelOverride/tipOverride`).
- Chaves novas `BO_SecBonus`, `BO_Bonus*` (EN e PT-BR); `BO_SearchHelp` cita o `+`.
- Compila em 1.6 e 1.7; deploy feito (11/nov). **Não testado no jogo.**
- **A testar**:
  1. Roupa de mecanitor (Biotech): seção com "+N Banda de mecanoides"; `+banda` acha; coluna de banda aparece e ordena.
  2. Jump pack / cinto de escudo / pacote de baixo escudo: habilidade, cargas e energia.
  3. Braço biônico: "Instala", eficiência da parte e efeitos.
  4. Gestador: custo de banda, classe de peso.
  5. Drogas (go-juice, cerveja): alegria, vício, efeitos; refeições sem a marca na lista, mas com nutrição no detalhe.
  6. Fixar 2-3 roupas de mecanitor: linhas de bônus no Comparar.
  7. `Player.log` sem erro novo ao abrir várias bancadas (com o modo dev, linhas `Bonus source ... failed` indicam um comp de mod que não lê sem uma coisa real; mandar se aparecerem).

## Correções do bill: passos, presets, Ctrl/Shift e clique na fila (11/nov/2026) - instalado (1.6 e 1.7), **confirmado em jogo pelo usuário (12/nov, todos os testes)**
Pedido do usuário com 3 prints (aba compacta da bancada de alfaiataria e cartão de munição x500):
- **Ctrl/Shift invertidos na tooltip** (verificado): o padrão vanilla é Ctrl = x10 e Shift = x100 (`Data/Core/Defs/Misc/KeyBindings/KeyBindings.xml`, `ModifierIncrement_10x`/`_100x`, lidos por `GenUI.CurrentAdjustmentMultiplier`). O código seguia o jogo, mas as tooltips diziam "Shift x10, Ctrl x100". Agora as tooltips montam o texto com as teclas que o jogador configurou (`Ui.StepKeysTip`, chave `BO_StepKeys`), e os passos continuam com as teclas do jogo (`Ui.StepMult`).
- **Presets** (volta ao comportamento pedido): o 1º clique num preset define o valor e marca o botão; clicar de novo no preset marcado soma o valor dele (1 → 5 → 10...). O -/+ sempre anda 1 unidade (1 repetição ou 1 lote em "Até X"); o preset não muda mais o passo. Ctrl/Shift multiplicam o preset e o -/+. Trocar de modo marca o 1º preset; Max desmarca; trocar de receita marca o preset igual ao valor (`RecipeDetail.qtyMark/MarkFor/QtyUnit`, chave `BO_PresetTip` reescrita, `BO_StepTip` nova).
- **+/- do cartão da fila em lotes**: em "Até X" o passo é o lote da receita (`RecipeEntry.TargetStepOf`: `targetCountAdjustment` ou o count do produto), munição x500 anda de 500 em 500; valores fora do lote encaixam (501: + vai a 1000, - vai a 500) e o mínimo é 1 lote. "Fazer X vezes" continua de 1 em 1 (repetições). `BO_QStepTip` removida (usa `BO_StepTip`).
- **Clique no 1º cartão da fila (alfaiataria) não abria o detalhe**: causa **não confirmada** (o código do clique é o mesmo para todas as bancadas). Hipótese: a receita da tarefa não está entre as receitas que a bancada oferece agora (`RecipeEntry.Build` filtra `AvailableNow`/`AvailableOnNow`) ou é outro def com o mesmo produto, e o `SelectRecipeOf` antigo só aceitava o mesmo `RecipeDef`. Agora `RecipeEntry.ForBill` procura pelo def, pelo defName e pelo produto; se nada casar, a aba compacta mostra uma entrada só de detalhe (`RecipeEntry.ForRecipe`, campo `extra` do `Patch_BillTab`). Com o modo dev ligado, cada clique num cartão grava no `Player.log` uma linha `[BillOrganizer] Queue click: ...`. Se ao clicar não aparecer nada, o clique nem chegou ao cartão, e a causa é outra.
- **Revisão por workflow de agentes (2 revisores + verificação adversarial)**: 7 achados confirmados; corrigidos:
  - o - do cartão "Fazer X vezes" ressuscitava tarefa concluída (0 → 1);
  - o - do detalhe em "Até X" caía para 1 item (agora o mínimo é 1 lote);
  - o +/- do cartão "Até X" não movia o "retomar em" (`unpauseWhenYouHave`) junto com a meta;
  - a entrada só de detalhe deixava adicionar uma receita indisponível (agora recusa);
  - o clique na fila da janela não limpava os filtros (coluna, munição, "só com estoque") e podia mostrar outra receita.
  Ficou de fora: a janela não tem a entrada só de detalhe (só a aba compacta). Achado não pedido: o botão "colar" vanilla do `ITab_Bills` é desenhado na posição fixa (372-396, 3-27) e fica por cima do chip "Ações" quando há algo copiado.
- Instalado em `Bin/` e `Mods/` (build 1.6 + 1.7, 11/nov).
- **Confirmado em jogo (12/nov, 4 prints do usuário):** clicar no 1º cartão da alfaiataria (gorka) abre o detalhe; os presets definem e somam (121 em "Fazer X vezes"); a tooltip mostra as teclas do jogo ("LCtrl+click x10, LShf+click x100": nomes curtos do `KeyCode.ToStringReadable` do jogo); o + do cartão "Até X" anda por lote (.338 x200: 320/200 → 320/400). A causa exata do clique da alfaiataria não foi registrada (o log `Queue click` não veio), mas a seleção tolerante resolveu.
- **A testar**:
  1. Alfaiataria: clicar no 1º cartão (gorka, Devilstrand) abre o detalhe. Com dev ligado, mandar a linha `Queue click` do log.
  2. Presets: clicar 5 → 5, 5 de novo → 10; + → 11. Ctrl+clique no 5 marcado → +50; Shift+clique → +500.
  3. As tooltips do -/+ e dos presets mostram "Ctrl+click x10, Shift+click x100".
  4. Cartão "Até X" de munição x500: + vai a 1000, Ctrl+ soma 5000; com 501, - vai a 500.

## Presets de quantidade e opções do mod (10/nov/2026) - instalado em `Bin/` (1.6 e 1.7), **NÃO testado no jogo**
- **Opções do mod "somem"** (relato de jogador na Oficina): ao desmarcar e marcar a lista de recursos ou a aba compacta, os sliders e a ordenação sumiam. Causa: o `Listing_Standard` quebrava o conteúdo numa 2ª coluna fora da área visível. Corrigido com `maxOneColumn = true` (`BillOrganizerMod.DoSettingsWindowContents`).
- **Presets de quantidade** (pedido do usuário): passaram a ser múltiplos 1x/5x/25x/50x de uma unidade.
  - "Fazer X vezes": `1 5 25 50` (repetições).
  - "Até X": múltiplos do lote (`TargetStep`); granada x10 dá `10 50 250 500`, munição do CE `200 1000...`.
  - O preset clicado também vira o passo do -/+ e fica destacado. O passo continua guardado depois de Max ou do -/+ e é multiplicado por Shift/Ctrl. Max não mudou. A tooltip é `BO_PresetTip`, que substitui `BO_PresetBatchTip`.
  - **1º teste (10/nov, print do usuário):** ao trocar de "Até X" (200 itens) para "Fazer X vezes", o valor ficava 200 e virava 200 repetições. Corrigido: trocar de modo zera para uma unidade (1 repetição ou 1 lote) e volta o passo para 1x. Clicar no modo que já está ativo não faz nada.
  - **Confirmado em jogo (10/nov, 4 prints do usuário):**
    - Granada de gás, "Fazer X vezes": `1 5 25 50 Max`.
    - Granada de gás, "Até X": `20 100 500 1000 Max`. O passo é 20 e não 10, porque `TargetStep` usa o `targetCountAdjustment` da receita (20), que é maior que o lote de 10.
    - Munição do CE: `200 1000 5000 10000 Max`, todos cabem.
    - A troca "Até X" → "Fazer X vezes" volta para 1.
  - As opções do mod (`maxOneColumn`) continuam **sem teste**.
- **"Copiar fila"** (pedido do usuário, 10/nov): o botão de copiar do cartão copia só uma tarefa, e o usuário não o achava (o ícone vanilla parece "colar"). Novo item "Copy queue / Copiar fila" no topo do menu Ações da fila (`Templates.CopyQueue`), que guarda em memória os clones de todas as tarefas.
  - O "Colar" usa o que foi copiado por último (`Templates.TryPasteQueue`). Uma cópia de tarefa feita depois troca o `BillUtility.Clipboard` e passa a valer.
  - A fila copiada só cola no mesmo jogo: as tarefas mantêm as referências (estoque, colono), como na cópia vanilla.
  - O que não serve na bancada ou passa do limite de 15 é pulado, com aviso (mesmo `Paste` dos modelos).
  - Instalado em `Bin/` (1.6 e 1.7), **não testado no jogo**.
  - **A testar**: copiar uma fila de 3+ tarefas, colar em outra bancada do mesmo tipo e de outro tipo (aviso de puladas). Depois copiar um cartão e colar: deve colar só aquela tarefa.
- **A testar**:
  1. Desmarcar e marcar várias vezes "lista de recursos" e "aba compacta": as opções continuam visíveis.
  2. Granada de gás, "Fazer X vezes": clicar em 5 → o + leva a 10, 15; depois de Max, o + ainda soma 5.
  3. Granada de gás, "Até X": começa em 10; clicar em 50 → anda de 50 em 50.

## Oficina Steam e fonte fora de `Mods` (7/nov/2026)
- Publicado na Oficina pelo usuário (ID 3835654283). O 1º upload levou a pasta inteira (Docs, Source, .git...): o fonte foi para `RimWorld/_dev/src/BillOrganizer` e `Mods/BillOrganizer` virou cópia limpa gerada por `tools/deploy.ps1` (chamado pelo `build.ps1`). Build 1.6 + 1.7 e deploy rodados com sucesso; a cópia tem só `About/`, `LoadFolders.xml`, `1.6/`, `Bin/`. **Falta:** "Atualizar na Oficina Steam" no jogo (troca conteúdo e thumb) e confirmar que o jogo carrega a cópia nova sem erro (**não testado no jogo**).
- Novo `About/Preview.png` (1280x720, `tools/gen_preview.py`) e descrição da Oficina em `Docs/WORKSHOP_DESCRIPTION.txt` (BBCode; colar à mão na página, o jogo não reenvia descrição). Detalhes: `../MountedArms/Docs/TOOLING.md` (seção "Fonte x cópia publicada").
- Achado da checagem: a `<description>` do `About.xml` (texto do jogo) está defasada (sem aba compacta, cartões, templates, link de bancadas, prioridade). Não alterada nesta rodada.

## Nomes curtos dos subgrupos (5/nov, 16:4x) - só Keyed (reiniciar o jogo), **NÃO testado no jogo**
**Confirmado em jogo (5/nov, 3 prints)**: as famílias aparecem com nomes curtos (gemas: Acceleration, Conservation, Force...; robôs: Builder, Cleaner, Crafter, ER, Hauler, Kitchen, Omni).
- **Achado**: com o teto de 230 px a árvore cortava "Snipers and anti-..." e "Launchers and he...". Nomes trocados para caber no 3º nível (cerca de 15 caracteres):
  - EN: Snipers, Heavy weapons;
  - PT-BR: Snipers, Armas pesadas, SMGs (era Submetralhadoras), Chapéus (era Chapéus e capuzes).
- A árvore já mostrava o nome inteiro na tooltip. Sem mudança de código.
- **A testar**: depois de reiniciar o jogo, os nomes inteiros na árvore da mesa de usinagem e da alfaiataria (EN e PT-BR).

## Nomes das famílias (5/nov, 16:25) - **confirmado em jogo (5/nov)**
**Confirmado em jogo pelo usuário (5/nov, 5 prints + "Dev" da mesa de usinagem)**:
- **Stats por material**: com Arcalleum, 8.8 mm / 26 MPa / 160%, valor $1325 e "Can make 0" em vermelho; em "Qualquer" volta a faixa.
- **Alfaiataria**: Cabeça 2/18/46 e Torso 5/27/34.
- **Famílias** de gemas (8x3) e robôs (7x5).
- **Distância na mesa de usinagem** (192): fuzis 61, snipers 25, metralhadoras 25, pistolas 23, lançadores 19, escopetas 15, SMG 13, primitivas 5, não letais 4, utilitários 2. A munição do CE tirou de Fuzis as escopetas e SMGs sem tag. Em Fuzis sobraram fuzis de assalto, de batalha e de ferrolho, mais 4 armas de alavanca/tiro único com munição de pistola (Winchester 94, BigBoy, DoubleBadger, Savage 2400): aceitável, sem regra nova.
- Sem erro do mod no log (os erros do log são de VehiclesCompat e do RoM).

Defeito corrigido (16:25):
- Os nomes das famílias cortavam justamente no que distingue ("Arcane Gem of F...", "Builder(I) Base St...").
- `Grouping.FamilyLabels` tira as palavras comuns a todas as famílias do nó, no começo e no fim, e a marca de nível "(I)": fica "Force", "Healing", "Builder", "Cleaner". Se dois nomes ficarem iguais ou vazios, volta ao nome inteiro.
- Testado em Python com os nomes dos prints.
- **A testar**: gemcrafting e oficina de robôs com nomes curtos e distintos na árvore.

## 3º nível, famílias e stats por material (5/nov, 15:25) - **confirmado em jogo (5/nov)**, nomes das famílias corrigidos às 16:25
Pedido do usuário: "pode fazer tudo" (as subdivisões propostas na rodada 15:18), e o detalhe deve mostrar os stats do material escolhido ("Qualquer" mantém a faixa).
- **Stats por material** (`RecipeDetail.PickedRow`): com um material escolhido, Cortante/Contundente/Calor/Frio mostram o valor dele (sem faixa) e o Valor vira o preço com esse material; em "Qualquer" continua a faixa. Na lista MATERIAIS a linha escolhida fica marcada e sempre visível, e **clicar numa linha escolhe o material** (de novo = qualquer).
- **3º nível** (`Grouping.AddThirdLevel`, chamado no `RecipeEntry.Build`): só quando o nó tem mais de 15 receitas naquela bancada e pelo menos 2 tipos.
  - **Distância** (`RangedSub`): Pistolas / Submetralhadoras / Escopetas / Fuzis / Snipers e antimaterial / Metralhadoras / Lançadores e pesadas / Primitivas / Não letais / Utilitários. Ordem: tags (as do CE e as do jogo), depois os dados do CE lidos em jogo (família da munição, pente, rajada; `CeCompat.AmmoFamily`), depois o techLevel; o que sobra vira Fuzis. Simulado no dump só com tags (mesa de usinagem, 192): pistolas 26, SMG 11, escopetas 4, fuzis 72, snipers 25, metralhadoras 24, lançadores 19, primitivas 6, não letais 4, utilitários 1. Em jogo, a munição do CE deve tirar de Fuzis as escopetas e SMGs sem tag; o binóculo-rádio vai para Utilitários pelo verbo.
  - **Cabeça** (`HeadSub`): Capacetes / Rosto e pescoço / Chapéus e capuzes. Alfaiataria: 2 / 18 / 46.
  - **Torso** (`TorsoSub`): Armaduras / Agasalhos / Roupas. Alfaiataria: 5 / 27 / 34.
  - **Famílias** (nó com mais de 12 receitas): produtos com sufixo de nível (`_II`...`_V`, `_minor/_major`, `Mk2`) viram um nó por família, com o nome do produto de defName mais curto (robôs do Misc. Robots++, gemas do RoM). Rótulos das famílias não conferidos em jogo.
- Dump "Dev": colunas `sub` (3º nível previsto) e `ceAmmo` (categoria/família/pente).
- **A testar**:
  - mesa de usinagem > Distância com os 10 subgrupos e o que ainda sobra em Fuzis (mandar o "Dev");
  - alfaiataria > Cabeça e Torso divididos, e a ferraria sem divisão;
  - Robôs e Magicita refinada por família;
  - colete com Hyperweave: Cortante 2 mm, Valor e linha marcada; em "Qualquer" volta a faixa;
  - clique numa linha de material.

## Retorno do teste das 14:36 + rodada 15:18 (5/nov) - DLLs 1.6 e 1.7 instaladas às 15:18, rodada **NÃO testada no jogo**
**Confirmado em jogo pelo usuário (5/nov, 5 prints + dump "Dev" de 38 bancadas):**
- "Até X" começa em 200 na munição, com atalhos 5/10/25/200 e -/+ de 200.
- Clique na 2ª linha do cartão abre o detalhe no 1º clique, e arrastar continua funcionando.
- Capacete composto sem chip de material (print).
- Roupas > Torso com flak jacket, armaduras e vacsuit; botão "Dev" funcionando.
- Sem erro do mod no `Player.log`.
- Dump revisado por workflow (3 auditores + 3 verificadores, 1395 receitas): **nenhuma das 245 roupas está no slot errado.**

Defeitos achados e corrigidos (15:18):
- **Meta 500 num capacete**: a marca "valor posto por nós" ficava em cada painel (`RecipeDetail`), separada entre janela e aba e perdida ao reabrir. Agora fica em `Memo.cfgAuto`, salva nas opções (9º campo; linhas antigas contam como automáticas).
- **"Dá para fazer" com material escolhido**: contava todos os tecidos ("79, limited by Fabric or leather" com Hyperweave). `Availability.BatchesWith` conta só o material escolhido (mais as opções que não são material), com tooltip `BO_CanMakeStuffTip`; o "Máx" usa o mesmo número. Limite do cálculo: não divide o estoque entre linhas que usam o mesmo material.
- **Processamento**:
  - `DestroyWeapon`/`DestroyApparel` caíam em Outros: o CE põe torretas minificadas sob Weapons e o RoM põe gemas sob Apparel; agora também conta a categoria (`ProcIn`);
  - reciclagem com produto (`RecycleMechAmmo`, `ReclaimMechAmmo`, `DismantleWarhead`) vai para Processamento (`IsSalvage`: entrada só munição/arma/roupa ou produtos = custo do item; cadáver fora);
  - `Make_StoneBlocksAny` vai para Matérias-primas > Blocos (pelos `butcherProducts` dos pedaços).
- **Rituais** (novo grupo): receita de subclasse de `RecipeDef` sem produto (rituais `TM_*MR` do RoM, inclusive os de esqueleto).
- **Armas**: Arremesso (novo nó irmão de Granadas: verbo de uso único ou `AmmoDef` do CE sem tag "Gun"; facas, pedras, machadinhas, dardos) e a categoria própria do mod sob Weapons (focos mágicos do RoM em "Magic weapons").
- **Nomes repetidos** entre mods ("lab coat", "gas mask", "backpack") ganham " (mod)" no nome.
- Dump "Dev" agora também grava verbo, techLevel e classe da receita (dados para a subdivisão de Distância).

**Proposto e não feito (decisão do usuário):** subdividir Distância (246 itens: pistolas, SMG, escopetas, fuzis, snipers, metralhadoras, lançadores, primitivas); 3º nível em Cabeça e Torso na alfaiataria (66 cada); famílias de robôs (Misc. Robots++, 7x5) e de gemas (RoM, 8x3).

**A testar**:
- capacete/armor vest no "Até X" sem o 500 herdado;
- "Dá para fazer" com Hyperweave escolhido;
- fundição com Destruir arma/roupa em Processamento;
- Arremesso e "Magic weapons" na árvore de armas;
- "(Anomaly)"/"(Vanilla Apparel Expanded)" no lab coat;
- grupo Rituais no círculo mágico;
- novo "Dev" com as colunas extras.

## 4 correções pedidas pelo usuário (5/nov, 14:36) - **confirmadas em jogo (5/nov)**, ver a rodada 15:18
Pedido com 5 screenshots (detalhe da munição .338 Norma, janela vanilla de detalhes, fila, capacete composto, árvore com Torso = 1). Fatos conferidos no IL 1.7 descompilado: `Dialog_BillConfig` anda o "Até X" em `recipe.targetCountAdjustment`; `Bill.IsFixedOrAllowedIngredient` aceita ingrediente fixo sem olhar o filtro da tarefa; `ReorderableWidget.Reorderable` não consome o MouseDown e `Widgets.ButtonInvisible` = `GUI.Button` (consome).
1. **"Até X" em lotes** (`RecipeDetail.DrawOptions`, `RecipeEntry.TargetStep`): com lote > 1 (munição CE x200) o último atalho (50) vira o valor de 1 lote (tooltip `BO_PresetBatchTip`); o -/+ anda 1 lote (x Shift/Ctrl como no vanilla); ao escolher a receita ou entrar em "Até X", a meta sobe para 1 lote se estiver abaixo (um valor escolhido pelo jogador não é trocado; o que nós pusemos é). "Fazer X vezes" continua 5/10/25/50. `Configure` ajusta o "retomar quando tiver" junto da meta (como o vanilla).
2. **Clique na fila** (`QueuePanel.DrawRow`): seleciona no MouseDown em qualquer parte do cartão (menos a coluna das setas; os botões já consomem o próprio clique), sem `Use()`, então o arrastar continua. Antes era `ButtonInvisible` só na 1ª linha até o fim do nome (dependia do MouseUp no mesmo controle IMGUI). `IsSelectedBill` compara a receita exata (antes `StartsWith` do defName).
3. **"No material allowed" falso** (`BillCard.Compute`): candidatos a material = só ingredientes **não fixos** (dentro do `fixedIngredientFilter`). Receita só com ingredientes fixos (capacete composto: aço + devilstrand) fica sem chip.
4. **Grupos revisados** (workflow de 6 agentes, só leitura, sobre o XML de Core/DLCs/CE/CE Armory/VAE/VFE/RimDark/RoM e outros ativos, com verificação cética):
   - Roupa (`RecipeEntry.ApparelSlot`): Neck não é cabeça; Torso vence; camadas de acessório só valem sem camada de corpo (OnSkin/Middle/Shell); cabeça por UpperHead/FullHead/Eyes/Teeth/Mouth ou camada Overhead/EyeCover/OnHead/StrappedHead; Shoulders/Arms com camada de corpo = torso; mãos só com Hands ou as duas mãos; resto = acessório. ~90 itens saem de Cabeça para Torso (flak jacket, parka, duster, armaduras, vacsuit...), CE composite armor sai de acessório; escudos de VFE e braceletes de RoM viram acessório; a coleira de escravo continua em Cabeça (como no vanilla).
   - Armas (`Grouping.IsWeaponProduct/IsGrenade`): bebida/tora com "tools" (VBE lemonade) não é mais arma corpo a corpo; granada pela categoria Grenades primeiro (gás lacrimogêneo do VWE e More Useful Grenade não têm a tag), lançadores em WeaponsRanged ficam em Ranged (lança-granadas CE/VWE, hand mortar). More Useful Grenade move a categoria Grenades para Manufactured: nunca depender de Grenades estar dentro de Weapons.
   - Munição (`Grouping.Ammo`): pega a 1ª categoria sob "Ammo" (não só a 1ª); munição fora do CE usa a própria categoria com id `a:` (sabot do Rimatomics); `MortarShells` no lugar do "Shells" inexistente.
   - Outros (`Grouping.Other/NoProduct/Pawn`): receita com vários produtos classifica pelo 1º (arma do ArmaGear que devolve madeira e aço saía em "Outros"); sem produto: Processamento (açougue, fundir, destruir, cremar), Mecanoides (ressurreição); gestação de mech = Mecanoides; categoria no topo "Apparel"/"Weapons" de algo que não é roupa/arma é descartada (artefatos do RoM não criam um 2º "Roupas"); 1ª categoria logo abaixo da raiz cede para uma mais funda; "...Misc" ganha o 3º nível.
   - **Botão "Dev"** (só em modo dev, na janela e na aba): grava no `Player.log` `defName | produto | kind | caminho | camadas | partes | weaponTags | categorias` de cada receita da bancada.
- **A testar**: munição CE no "Até X" (começa em 200, atalhos 5/10/25/200, -/+ de 200); clique na 2ª linha / à direita do nome abre o detalhe na 1ª vez e o arrastar ainda funciona; capacete composto sem "No material allowed" e parka com o chip de material; Roupas > Torso com flak jacket/parka/duster; Granadas sem lança-granadas; "Dev" de 2-3 bancadas (alfaiataria, máquinas, munição) no log. Mods ativos fora do conjunto lido pelos agentes não foram conferidos.

## 4ª rodada da UX/UI: `FitName` reforçado (5/nov, 12:3x) - DLLs 1.6 e 1.7 instaladas, **NÃO testado no jogo**
Screenshots do usuário (munição ordenada por Batches, busca "40"). **Confirmado em jogo**: `(AP)`/`(FMJ)`/`(Sabot)` distintos nas linhas, "Used by:" sumiu da descrição (a seção "USED BY (6)" lista as armas), Value `0.21/0.25/0.99`, "Batches▾" inteiro, botão "x" da busca. Achado: `.30-30 Winchester cartridge ...` e `.45-70 Government (Sabo...` ainda cortavam o fim e perdiam o tipo e o lote, porque um começo "só palavra inteira" de menos de 8 caracteres (`.45-70`) era recusado. Verificação independente (2 agentes: revisão do código + 80 mil casos de teste com nomes no estilo CE, em Python) achou também erros de borda; tudo corrigido em `Ui.FitName`:
- **Começo curto**: se o corte em palavra deixa só um toco, vale o corte mais longo (`.45-70 Gove... (Sabot) x200`); uma palavra inteira curta (`20x82mm`, 5+ caracteres) também vale. Busca binária em vez de tirar um caractere por vez.
- **Substantivo repetido do CE** (`.30-30 Winchester cartridge (FMJ) cartridge x400`): o "cartridge" repetido é descartado (`.30-30 Winchester (FMJ) x400`), só quando a cópia está colada em fronteira de palavra.
- **Só palavras genéricas caem** depois do tipo (cartridge, shell, round, magazine, ammo, ammunition). Qualquer outra palavra ("Butcher (Human) corpse") fica; só se nem assim couber é que ela cai numa 2ª tentativa.
- **Bordas**: largura <= 0 devolve vazio (antes podia estourar `Truncate`), nome só com o verbo não vira vazio, `\n` vira espaço, regex com `\z`/Singleline, par substituto UTF-16 não é cortado, o corte final trunca o nome já sem as palavras genéricas.
- **Cache** por (nome, largura) e a fonte é restaurada (a função roda a cada quadro por linha); qualquer exceção devolve o nome original.
- Teste (port em Python, 79.977 casos, largura por caractere): 0 exceções, 0 estouros de largura, os 5 nomes das fotos mantêm o tipo e o lote a ~30 caracteres, nomes que só diferem no tipo nunca colidem em largura >= 26. **Restam** (aceitos): larguras extremas (<= ~21 caracteres) e nomes sintéticos com dois tipos seguidos (mantém o último).
- **A testar**: as linhas `.30-30 Winchester`/`.45-70 Government`/`20x82mm Mauser` com tipo e lote visíveis; que nada pisca ou fica vazio ao redimensionar a janela; sem erro no log.

## 3ª rodada de correções da UX/UI (5/nov, 12:01) - DLLs 1.6 e 1.7 instaladas, **NÃO testado no jogo**
Screenshots do usuário após as correções das 11:36. **Confirmado em jogo**: menu "Actions" sem tooltip por cima (cada item legível, "Pause all" na bancada vazia); Value visível na munição; `Slugthrower (Buck)... x100`/`(Slug)...`/`(EMP)...` distintos; "Rounds: All v". Achado e corrigido:
- **Nomes iguais de novo** (`.408 Cheyenne... x200` x6, `.338 Norma... x200` x3, `Slugthrower... x100` do Beanbag): o corte só protegia um "(tipo)" que estivesse colado no fim; em `.408 Cheyenne Tactical (AP-HE) cartridge x200` o "(AP-HE)" ficava no meio e sumia. `Ui.FitName` agora reconhece `<prefixo> (tipo) <palavras genéricas> x<lote>` e encurta nesta ordem: nome inteiro; sem o verbo ("Make "); **sem as palavras genéricas depois do tipo** (`.408 Cheyenne Tactical (AP-HE) x200`); começo cortado em palavra (`.408 Cheyenne... (AP-HE) x200`); só por último o fim do nome. O tipo e o lote nunca somem antes do prefixo. Lógica conferida com os nomes reais em simulação (largura por caractere); **a largura real em jogo não foi vista**.
- **"Used by:" vazio na descrição da munição**: não é bug nosso. O CE (`AmmoDef.cs`) acrescenta a linha "Used by:" no fim da descrição e deixa a lista de armas para a ficha de informação do jogo. Como o nosso painel já tem a seção "USED BY (n)" com os nomes, o `RecipeDetail` agora tira a linha pendurada (`CE_UsedBy` traduzido + ":" no fim; só quando a chave existe, ou seja, com o CE ativo).
- **Cabeçalho ordenado cortado** (`Batc...` com a seta): a seta agora ocupa 10 px e fica sobre a borda direita, então "Batches" cabe quando é a coluna do sort.
- **Value "0"** em munição barata (0.04): valores abaixo de 1 mostram duas casas (`0.04`).
- **A testar**: lista de munição com os `.408 Cheyenne`/`.338 Norma` distinguíveis; descrição da munição sem "Used by:" solto (e com a seção USED BY logo abaixo); ordenar por "Batches" com o título inteiro; Value `0.04`; sem erro no log.

## 2ª rodada de correções da UX/UI (5/nov, 11:36) - DLLs 1.6 e 1.7 instaladas, **NÃO testado no jogo**
Screenshots do usuário após as correções das 11:25. **Confirmado em jogo**: Value de volta na munição, "Rounds: All v" / "Rounds: Armor-piercing incendiary v" (botão com menu, ativo realçado), "Belts and accessories" inteiro, cortes em palavra inteira nos cartões (`6.5x39mm Caseless... x500`), ícone de Desvincular como corrente quebrada, "Pause all" no menu Actions de uma bancada vazia, `1300/1000` com barra. Defeitos achados e corrigidos:
- **Nomes iguais** (`Slugthrower... x100` x4, `WarB... (Incendiary)`): para manter o Value, o nome da receita tinha ficado com ~156 unidades e o corte escondia o "(Buck)/(Slug)". Voltei o piso do nome para **190** (a última coluna cai primeiro, como no desenho original; o Value some só em janela apertada) e estreitei Guns/Work/Skill/Value em 4 unidades cada (ganho de 16). Decisão: **o nome identifica a linha, vale mais que o Value** (que na munição é irrelevante); o Value continua no detalhe.
- `Ui.FitName`: se o corte em palavra deixa o começo com menos de 8 caracteres ("WarB"), corta o fim do nome em vez de deixar um toco ("WarBolter Rockets (Incend...").
- Cabeçalho de coluna ordenada: a seta ocupa 16 px (antes 20), então "Batches" não vira "Batch..." quando é a coluna do sort.
- **Tooltip cobrindo o menu**: depois de clicar em "Actions", a tooltip do botão ficava por cima de "Paste/Save template" (o mouse ainda estava no botão). `Ui.Chip` não registra mais a tooltip enquanto existe um `FloatMenu` aberto (vale para todos os chips).
- **A testar**: munição com nomes distintos (`Slugthrower (Buck) shell x100` etc.) e a coluna Value (pode sumir em janela estreita); menu Actions sem tooltip por cima; ordenar por "Batches" sem cortar o título.

## Correções após o 1º teste da rodada UX/UI (5/nov, 11:25) - DLLs 1.6 e 1.7 instaladas, **NÃO testado no jogo**
Teste do usuário (screenshots + `Logs\Player.log`): **o log não tem nenhuma linha do mod** (os erros são de VehiclesCompat, TorannMagic e SimpleSidearms/Stance_Warmup). Confirmado em jogo (screenshots): chip `1300/1000`/`4/50` inteiro com barra, "Done", coluna "Batches", "Can make 992 (496,000 items), limited by Uranium", "Copies", "More/Less" (expande), "Max" em "Do X times", dica curta, cabeçalhos da janela "Tarefas", ícones de Duplicar e Vincular. Defeitos achados e corrigidos:
- **Coluna Value sumia** na munição (Guns + 4 colunas): a árvore mais larga (+16 px) e a coluna Lotes (+4 px) estouravam o orçamento "o nome fica com 190 px" e o código derruba a última coluna. Agora o nome mantém **170 px** e a árvore tem largura **adaptativa** (190 a 230 px, `Window_RecipePicker.TreeWidth`, pelo maior rótulo visível), então "Belts and accessories" cabe quando há espaço.
- **Corte do nome no meio de uma palavra** (`(... x500`, `WarBolter R... (Incendiary)`): `Ui.FitName` agora corta na fronteira da palavra e nunca logo após "(" ("6.5x39mm Caseless (AP-HE)... x500").
- **Ícone de Desvincular** parecia "O/O"/porcentagem: redesenhado como corrente quebrada (dois anéis com abertura e pequenos traços); `tools/gen_ui_tex.py` atualizado.
- **Fila vazia mostrava "Retomar todas"**: agora "Pausar todas" quando não há tarefas.
- **Janela "Tarefas"**: contagem e prioridade com folga de 10 px da barra de rolagem (cabeçalhos alinhados), painel esquerdo proporcional (300 a 380 px) para os nomes de bancada.
- **Filtro de munição**: se todos os tipos cabem, são chips; senão **um botão "Munição: <tipo/Todos> v"** com menu (acabou o chip alfabético arbitrário + "+22"). Nova chave `BO_AmmoAll` (Todos/All); `BO_Clear` não é mais usada ali.
- Não mudei (decisões abertas com o usuário): o verbo "Make " continua saindo só quando o nome não cabe; chips `-`/`+`/`...` continuam sem caixa no cartão não selecionado (mesma cor do cartão); ícone vazio das receitas "Recycle..." (sem ícone no jogo).
- **A testar**: munição com Value visível em janela do tamanho do print; "Belts and accessories" inteiro; nomes longos cortando em palavra inteira; botão "Munição:" abre o menu e filtra (e limpar com "Todos"); ícone de Desvincular; fila vazia com "Pausar todas"; janela "Tarefas" com números longe da barra; sem erro no log.

## Rodada UX/UI (5/nov, tarde): cartão adaptável, estados, nomes, filtros, janela "Tarefas" - DLLs 1.6 e 1.7 instaladas (build validado, mesmos hashes), **NÃO testado no jogo**
Pedido do usuário (11 screenshots): o chip "Until 1000 (130..." cortava com espaço sobrando, e o pedido de revisar todo texto/aparência/instrução do mod com critérios de UX/UI, sem mudança drástica (o vocabulário "Until X / Do X times / Forever" fica). Base: pesquisa online (NN/g, Carbon, Maersk, Primer, SAP Fiori, Apple HIG...), a decompilação do `Bill_Production.RepeatInfoText` (vanilla mostra Until X como `atual/alvo`) e auditoria do código. Plano em `C:\Users\Felipe Pinto\.claude\plans\veja-essa-tela-do-iridescent-milner.md`.
- **Cartão da fila** (`QueuePanel.DrawRow`): sem teto de 120 px; o chip de modo usa o espaço que sobra (só reserva ~30 px para o chip de material) e testa formas do maior para o menor: Until X = `130/1000`, depois `1.2k/10k`, só então corta. Barra fina de progresso no chip (verde quando a meta foi atingida). Tooltip do chip explica o número (`BO_QTargetTip`) + como mudar. **Substitui** o "Até 50 (12 agora)" da rodada de 4/nov.
- **Estados**: "Concluída/Done" para "fazer X vezes" em 0 (antes aparecia "0x" + "not now"); meta atingida = número em verde, sem texto; "Pausada"; "Agora não" (com maiúscula) só quando o jogo diz que não dá. Texto de estado só aparece se couber; a faixa lateral (tooltip) sempre traz o motivo. Removida a tooltip grande que cobria os cartões (o "arraste para reordenar" foi para as dicas de subir/descer).
- **Ícones/botões**: Duplicar ganhou textura própria (`duplicate.png`; antes usava o ícone de Colar); Vincular/Desvincular bancada ganharam `link.png`/`unlink.png`; subir/descer 22x22; Delete separado por +4 px; `-`/`+` com dica do Shift/Ctrl.
- **Nomes adaptáveis** (`Ui.FitName/NameFit`): nome inteiro; se não couber, sem o verbo ("Make "/"Fazer ", pego da chave `RecipeMake` do jogo); se ainda não couber, corta no meio mantendo o sufixo ("6mm Railgun car... x500"). Usado nos cartões, na lista da janela e na lista da aba compacta. Tooltip só quando cortou.
- **Lotes**: o "x19" virou "19" (o "x" é lote/repetição em outros lugares); cabeçalho da coluna "Lotes/Batches" (coluna 62 px; antes "Can ..." cortado). Linha "Dá para fazer 19 (3800 itens), limitado por X" sem parênteses duplos (itens só se o lote tem mais de 1); separador de milhar a partir de 10000.
- **Detalhe**: "Bills" -> "Cópias/Copies"; rótulo da quantidade segue o modo ("Vezes"/"Meta"); **Máx** em "Até X" agora soma o que já se tem (antes ignorava o estoque); habilidade não se repete em Produção (fica em Requisitos); Valor no formato de dinheiro do jogo (`StatDefOf.MarketValue.ValueToString`); faixas "0 a 1.5 mm RHA" (palavra, unidade uma vez; evita "-2.4C - 10.8C"); descrição com "Mais/Menos" para expandir; seta no botão de material; números das tabelas em branco (a cor do atributo fica na barra e no cabeçalho; vermelho = falta).
- **Janela**: dica do topo curta (os atalhos foram para o "?"); chips de munição com rótulo "Munição:", menu "+N" para os que não cabem (antes sumiam), o ativo sempre visível; rótulos dos filtros mínimos medidos, não 134 px fixos; árvore 206 px; aba compacta: dica curta + tooltip, ponto de habilidade com tooltip. "Fila 3/15" nos dois lugares.
- **Janela "Tarefas"**: cabeçalhos "Bancada / Prioridade / Tarefas" (tooltips), contagem "-" (vazia), só o total (nada pausado) ou "ativas/total"; nome com tooltip quando corta; grupo com contagem; vazio por busca tem texto próprio; título com corte e tooltip.
- **Opções do mod**: agrupadas em Geral / Janela de receitas / Aba de tarefas / Bancadas / Recursos; o aviso do Nice fica logo abaixo das opções a que se refere; altura vem do conteúdo; `BO_SetMin` com valor na frente e dica.
- **Textos** (EN e PT-BR, mesmas 288 chaves): dicas longas encurtadas, mensagens sem "(s)" ("Tarefas apagadas: 3", "Nenhuma tarefa concluída."), confirmação de vínculo reescrita, menu de modelos "Nome: 3 de 5 tarefas cabem". PT-BR: **Vincular/Desvincular** (era "Linkar"), **modelo** (era "template") e **Desafixar** (era "Soltar"). Setas "+/-" de dobrar nas listas de recursos viraram as setas-textura do resto do mod; "Nenhum recurso encontrado" na busca de recursos.
- Chaves removidas: `BO_Qty`, `BO_QDragTip`, `BO_QToCount`, `BO_QTargetHave`.
- **A testar (tudo)**: Until 1000 com estoque 130 em janela larga e estreita ("130/1000" inteiro, "1.2k/10k"/"..." só quando falta espaço, também em PT-BR com material escolhido); "fazer X vezes" em 0 mostra "Concluída" verde, "Limpar concluídas" continua apagando; meta atingida; pausa; Duplicar/Vincular com os ícones novos (a textura `link`/`unlink` foi desenhada às cegas, 64 px); nomes longos de munição (CE) nos cartões e na lista; coluna "Lotes"; chips de munição + menu "+N"; descrição "Mais/Menos"; "Máx" em Até X; janela "Tarefas" (cabeçalhos alinhados com as colunas); tela de opções agrupada (altura, rolagem); EN e PT-BR; sem erro no `Player.log`. **Riscos**: a altura da tela de opções vem do quadro anterior (a 1ª abertura pode piscar); `Listing_Standard.Label(TaggedString, float, string)` compilou nas duas versões mas não foi visto em jogo.

## Prioridade de bancada entre tipos da mesma habilidade (5/nov) - **testado no jogo, todos os testes passaram (relato do usuário, 5/nov)**
- Pedido: Fabrication bench x Smithy (ambas Smithing; `DoBillsMakeWeapons` 115 x `DoBillsFabricationBench` 50 no jogo base) não respeitavam a prioridade da bancada, que só desempatava bancadas do mesmo WorkGiver.
- `Source/BenchOrder.cs` (novo): postfix (prioridade Last) em `Pawn_WorkSettings.WorkGiversInOrderNormal` e `...Emergency` (getters; campo privado `pawn` via `AccessTools.FieldRefAccess`). Prioridade efetiva do WorkGiver = maior `BenchPriority` entre as bancadas do jogador no mapa do colono com tarefas na fila e `def` em `fixedBillGiverDefs`; tabela por mapa a cada 120 ticks. Dentro de cada trecho da mesma `workType`, só os `WorkGiver_DoBill` trocam de lugar (ordenação estável por prioridade desc); tudo zero = lista original intacta; cache por lista (invalidado por mudança de prioridade/tabela/lista); exceção = lista original + `Log.ErrorOnce`.
- Opção `benchPrioAcross` (padrão ligada; `BO_SetBenchPrio`) nas configurações do mod. Tooltip do `+/-` reescrito.
- **Não cruza habilidades** (aba Trabalho). WorkGivers de mods sem `fixedBillGiverDefs` ficam fora.
- **A testar**: Fabrication com +1 e Smithy com 0, ambas com tarefas e um colono com Smithing: vai à Fabrication primeiro; invertendo, à Smithy; tudo 0 = como antes (Smithy primeiro); opção desligada = como antes; salvar/recarregar; sem erro no log; conflito com mods de aba de trabalho.

## Rodada 4/nov: clique na fila, "até X" com estoque, prioridade de bancada - **testada no jogo, todos os testes passaram (relato do usuário, 5/nov)**
- Clique no cartão da fila (ícone/nome) seleciona a receita no painel de detalhe: aba compacta (`Patch_BillTab.SelectRecipeOf`, limpa a busca se a receita estaria escondida) e janela (`Window_RecipePicker.SelectRecipeOf`: grupo Todos, busca limpa, troca a fila pelo detalhe). `QueuePanel.Draw(..., onSelect, isSelected)`; cartão selecionado realçado. A janela "Tarefas" não passa `onSelect`. **A testar**: o clique não atrapalha o arrastar/subir/descer.
- "Até X" mostra `Até 50 (12 agora)`: `QueuePanel.Have` usa `recipe.WorkerCounter.CountProducts(bill)` (o mesmo cálculo do `RepeatInfoText` vanilla, "12/50"), com cache de 0,5 s; chip de modo até 120 px. Chave `BO_QTargetHave`. **A testar**: texto cabe no chip, número bate com o vanilla.
- Prioridade por bancada (`BenchPriority.cs`): `GameComponent` salvo no save (thingIDNumber -> -5..+5) + postfix em `WorkGiver_Scanner.GetPriority(Pawn, TargetInfo)` somando a prioridade quando o giver é `WorkGiver_DoBill`. UI: `[-] n [+]` por bancada na janela "Tarefas" (Shift = 5; a janela fica escondida por padrão, ligar nas opções do mod); grupos ordenados por prioridade. **Limite**: só desempata bancadas do mesmo tipo de trabalho (JobGiver_Work escolhe o maior GetPriority e depois o mais próximo, dentro de cada WorkGiver); Fabricação x Pedreiro continuam pela aba Trabalho do jogo. **A testar** com duas bancadas do mesmo tipo, e salvar/recarregar.
- **Correção (print do usuário: +/- não reagiam)**: o `ButtonInvisible` da linha, desenhado antes, engolia o clique dos botões de prioridade. Agora os botões vêm primeiro e o clique da linha depois (regra de ouro 8). DLLs reinstaladas, **não testado**.
- Esta rodada também instalou a DLL 1.7 que faltava da lista de recursos organizada (continua não testada).

## Lista de recursos: sem sombra, texto com contorno (3/nov, 18:5x) - DLLs instaladas, **não testado**
- Pedido do usuário após o print: removida a sombra de texto do jogo (`DrawTextWinterShadow`); a lista fica 100% transparente e todo texto é desenhado com contorno escuro fino (`ResourceList.Out/OutFit`, 8 deslocamentos de 1 px) e cabeçalhos mais claros. Ícones sem contorno. **Ver legibilidade sobre áreas claras no jogo.**

## Ajustes da lista de recursos após o 1º teste (3/nov, 18:35) - DLLs 1.6 e 1.7 instaladas, **não testado**
- Pedido do usuário (print): tudo **recolhido por padrão** (agora `resExpanded` guarda o que fica aberto; busca expande tudo; o antigo `resCollapsed` é ignorado), **Categoria/Qtd./Nome saíram da lista e foram para as opções do mod** (rádio "Ordem da lista de recursos"), lista **menos larga** (padrão 200) e **sem fundo** (só a sombra de texto do jogo), linhas de 22 px, botão de recolher tudo removido. A seção "Fixados" também começa recolhida (mesma regra).

## Lista de recursos organizada à esquerda (3/nov, noite) - compila em 1.6 e 1.7; **DLL 1.7 NÃO instalada** (jogo aberto travou a cópia; rodar `build.ps1` com o jogo fechado), **não testado no jogo**
- Motivo: a lista da esquerda (modo simples, ícone + número) continuava igual; o painel lateral de fixados só aparecia após o Ctrl+clique. A janela "Contained items" (storage) vista no print **não é nossa**.
- `Source/ResourceList.cs`: prefix (prioridade Last) em `ResourceReadout.ResourceReadoutOnGUI` desenha uma lista própria (mesma fonte `resourceCounter.AllCountedAmounts`): busca, ordenação Categoria/Qtd./Nome, nome ao lado do número, seções por categoria raiz (`resourceReadoutRoot`) recolhíveis (estado salvo; busca expande tudo), seção "Fixados" no topo. Ctrl+clique fixa/solta, botão direito = mínimo/fixar/soltar, tooltip com receitas que usam o recurso. Qualquer exceção liga `ResourceList.Failed` e volta a lista do jogo.
- Opções: `resList` (ligada), `resListW` (160-420, padrão 230). Com a lista nova ligada o painel lateral antigo (`ResourcePanel.Draw`) não é desenhado.
- **A testar**: aparência/largura com ~40 recursos, rolagem, busca ("aço"), recolher e persistir, fixar/soltar, mínimo, desligar a opção = lista do jogo, sem erro no log, conflito com outro mod que mexa no readout.

## Linha de inserção do arrasto (3/nov, noite) - **confirmado em jogo pelo usuário, trabalho encerrado**
- **Arrastar funciona no jogo (relato + screenshots do usuário)**; a correção do id de grupo funcionou. Defeito: a linha de inserção do jogo passava pelo meio do cartão porque o retângulo do Reorderable era só a faixa do nome. Agora é a linha inteira (
ow), então a linha cai entre os cartões. Testar: arrastar para o fim e para o topo.

## Reordenar, 2ª tentativa (3/nov, noite) - instalado, **não testado**
- O ReorderableWidget também não arrastou (screenshot do usuário). Hipótese: NewGroup devolve -1 fora do Repaint e o Reorderable grava o grupo no MouseDown, então o grupo clicado nunca batia; agora reaproveito o id do último Repaint (lastGroup). **Hipótese, não confirmada.**
- Garantia: botões **subir/descer** em cada cartão (coluna à esquerda do ícone; Shift = topo/fim) usando stack.Reorder; não dependem de eventos de arrasto. Testar: subir o Ballistic shield ao topo com Shift; testar de novo o arrasto.

## Retorno do usuário nos cartões + arrastar + ícone (3/nov, noite) - DLL e textura instaladas, **não testado**
- **Testado no jogo (relato + screenshots)**: cartões na aba compacta e na janela, chip de material (ex.: "Steel"), "Any material", "not now", modo/-/+/..., menu Ações: aparecem e funcionam. **Arrastar para reordenar não funcionava.**
- **Arrastar**: causa provável = eventos IMGUI crus dentro da janela/aba não chegam (o jogo trata o arrasto no nível do WindowStack). Trocado pelo ReorderableWidget vanilla (NewGroup + Reorderable por cartão, descompilado em 1.7); a área de arrasto é ícone+nome (não a linha dos controles). Isso também vale para a fila antiga da janela (nunca tinha sido testada). **Testar**: arrastar na aba, na janela e em "Tarefas"; a linha de inserção; soltar no mesmo lugar não muda nada.
- **Ícone**: Catalog.png (botão da barra "Bills" e botão "Receitas" da bancada) trocado de "lista com seta" para uma **prancheta com checklist** (	ools/gen_icon.py). **Testar** como fica na barra (tamanho/contraste).
## Cartão moderno de tarefa na fila (3/nov, fim do dia) - DLL instalada em `Bin/1.6` e `Bin/1.7`, **não testado no jogo**
- `QueuePanel` agora desenha **cartões** (58 px): ícone do produto, nome, **chip de material**, chip de modo (clique alterna X vezes / até ter X / sempre), `-`/`+`, `...` (abre o `Dialog_BillConfig` vanilla), pausar/copiar/duplicar/apagar, arrastar para reordenar. Vale na janela (painel Fila), em "Tarefas" e na aba compacta.
- **Aba compacta**: `Patch_DoListing.Prefix` (Hook.cs) pula a fila vanilla e chama `Patch_BillTab.TryDrawQueue` (`QueuePanel.Draw(..., compact: true)`: botão Adicionar tarefa que abre a janela + menu "Ações" com colar/templates/pausar/limpar). Se der exceção, loga e volta à fila vanilla até reiniciar o jogo. Só com `integration = Auto` e a aba compacta ativa (Nice ligado = nada muda).
- **Material** (`BillCard.cs`): compara o `ingredientFilter` da tarefa com o padrão da receita. Padrão e sem escolha de material = nada; padrão com escolha = "Qualquer material" apagado; 1 liberado = ícone + nome; 2-3 = só ícones; mais = "N de M liberados"; zero = vermelho. Tooltip lista os liberados com estoque. Clique (só quando o produto é feito de material, 2+ opções) abre menu para restringir a tarefa já na fila ou voltar ao padrão (mesma regra de `RecipeEntry.Configure`).
- **A testar**: aparência/larguras do cartão em 400 px (aba) e na janela; arrastar sem disparar os botões; trocar material de tarefa existente (Make vest) e ver o filtro no "Detalhes..."; modo cíclico; "Detalhes..." abre o diálogo; link de bancadas sincroniza a troca de material/modo; fallback vanilla (forçar? só via log); Nice ligado intacto; sem exceção no log.
- Observações do screenshot do usuário (hint do topo e cabeçalho "Can ..." cortados) **ainda não tratadas**.

## "Dá para fazer N" + painel de recursos fixados (3/nov, noite) - DLL instalada em `Bin/1.6` e `Bin/1.7`, **não testado no jogo**
- **Cálculo** (`Availability.EvaluateBatches`): lotes = mínimo, sobre as linhas de ingrediente conhecidas, de `floor(haveBase / needBase)` (unidades da receita: volume/nutrição); linhas que aceitam o mesmo def são conferidas num pool compartilhado (busca binária, `FitShared`). Itens = lotes x `products[0].count`. Fonte = `ResourceCounter` (storage, mesmos números do painel à esquerda; não vê itens soltos, proibidos nem alcance). Linhas desconhecidas (armas/roupas) não limitam; sem nenhuma linha conhecida = desconhecido (sem número).
- **UI**: coluna/ordenação "Dá para fazer" (`StatTable.Make`, na janela); badge "xN" na lista da aba compacta; no detalhe "Dá para fazer: N (= M itens) (limitado por X)" com tooltip do gargalo, barra amarela no ingrediente limitante, botão **Máx** ao lado dos presets de quantidade (lotes no modo "fazer X vezes", itens em "até ter X").
- **Painel de fixados** (`ResourcePanel.cs`, patches manuais em `TryPatch`, fora do `PatchAll`): Ctrl+clique num recurso da lista à esquerda fixa/solta (só itens, não categorias); painel à direita da lista com ícone, nome, quantidade, mínimo por item (botão direito > definir; vermelho abaixo, amarelo até 1,5x), recolhível; tooltip com as receitas que usam o recurso e seu "x lotes" (`PinRecipes`: entradas por tipo de bancada, uma por frame, só no tooltip). Opção `pinPanel`. Salvo em `pinnedRes`/`pinMin` nas opções do mod.
- **Nome**: só o nome de exibição mudou (About e opções: "Colony Organizer"); packageId, pasta, namespace e DLL ficam (ver DECISIONS).
- **A testar**: N bate com o contador da esquerda e com "fazer até ter X"; refeição com mistura (nutrição); receita com 2 linhas de mesmo material; munição CE (itens = N x lote); Ctrl+clique fixa e persiste; painel não sobrepõe nada importante (fica em x=130, y=7); sem erro de patch no log (`Could not patch the resource readout`); tooltip das receitas na primeira abertura (soluço?); Máx nos dois modos. Ainda sem clique no recurso para abrir a bancada.

## Rodada de melhorias (3/nov), fases 1-2 - compila em 1.6 e 1.7, **não testado no jogo**
Plano em `C:\Users\Felipe Pinto\.claude\plans\pode-fazer-um-plano-distributed-phoenix.md` (análise visual/funcional + link de bancadas). Feito até agora:
- **Bugs**: `Templates.Ensure` guarda `.bak` do arquivo ilegível antes de qualquer escrita; `Templates.Scrub` agora zera todo campo de referência (Thing, ILoadReferenceable, ISlotGroup, Zone) da cadeia da classe da bill, não mais uma lista fixa, e loga falhas; `QueuePanel.Draw` checa `stack` antes de usar `billGiver`.
- **Não alterado de propósito**: `Grouping.IsAmmo` usa `StartsWith("Ammo")` de propósito (categorias do CE são `AmmoAdvanced`, `Ammo556x45NATO`...); trocar por `==` quebraria o CE. O arrastar da fila já cancela se o botão sobe fora da janela (`Input.GetMouseButton`), então não mexi.
- **Visual**: título do detalhe com altura real + tooltip; subtítulo truncado com tooltip; descrição com "..." e tooltip quando passa de 110 px; rolagem do detalhe volta ao topo ao trocar de receita; cabeçalho nas duas colunas numéricas de materiais; `Ui.Faint` 0,3 -> 0,42; novo `Ui.TextFit` (corta com "..." e dá tooltip) no rótulo "Mín", hint do topo, cabeçalhos de coluna e linhas do `DrawStats`; faixa de estoque com tooltip (Keyed `BO_StockReady/Missing/Unknown`) e cinza quando desconhecido; linha selecionada com filete Accent e nome esmaecido quando falta material (janela e aba); aba compacta com coluna esquerda proporcional (`LeftW` = 43,5% da largura, 300-460).
- **Testar**: nomes longos (VWE/CE), descrição longa, trocar de receita com o detalhe rolado, aba estreitada para 700, tooltips da faixa.

## Rodada de melhorias, fases 3-4 (3/nov) - DLL instalada em `Bin/1.6` e `Bin/1.7` (build sem `-Out`, jogo fechado), **não testado no jogo**
- **Link de bancadas** (`Source/BenchLink.cs`, opção `linkBenches`, ligada): botão "Linkar bancada" em toda `Building_WorkTable` do jogador; escolhe outra bancada **do mesmo tipo (mesmo def) e mesmo mapa**; a fila da bancada de origem é copiada para a outra (pede confirmação se ela perde tarefas). Depois, qualquer edição (adicionar, apagar, reordenar, pausar, quantidade, filtro, etc.) numa bancada do grupo é copiada para as outras; "fazer X vezes" é decrementado em todas quando uma conclui (`Patch_IterationCompleted`). Salvo no save (`BillLinkManager : GameComponent`). "Desfazer link (N)" tira a bancada do grupo; bancada destruída/minificada sai sozinha.
- Como funciona: polling a cada 0,3 s em `GameComponentUpdate` (roda também pausado) com assinatura (hash) da fila; a bancada que mudou vira origem. Mesma forma de fila = copia campo a campo (`CopySettings`), forma diferente = apaga e recria por `Bill.Clone()`. Não há patch em setters, então vale para qualquer aba (vanilla, Nice, nossa).
- **Testado no jogo (relato do usuário, 3/nov)**: linkar e editar com o jogo pausado, concluir itens, salvar/recarregar (grupo persiste), destruir bancada e desfazer link, recusa de tipos diferentes. **Problema achado**: com "fazer 1x", as duas bancadas mostravam o item e duas pessoas podiam começá-lo ao mesmo tempo (produz 2). **Correção (não testada)**: `Patch_ShouldDoNow` + `BillLinkManager.ReservedByPeers`: enquanto colonos já trabalham na mesma tarefa das outras bancadas do grupo e cobrem tudo o que falta do contador, a tarefa desta bancada fica "agora não" para os demais (nunca bloqueia quem já está trabalhando nela). Vale só para o modo "fazer X vezes"; "até ter X" depende do estoque e pode passar um pouco.
- **Tooltip dos nomes truncados (3/nov)**: faltava na lista da janela e da aba compacta; agora `Ui.TextFit` (corta com "..." e dá tooltip com o nome inteiro). **Não testado.**
- **Riscos / a testar**: linkar 2 bancadas iguais e editar uma; pausado também sincroniza?; concluir itens nas duas; salvar/recarregar (grupo persiste?); destruir uma bancada; desfazer link; tentar linkar tipos diferentes (deve recusar); bills com UFT (peças inacabadas) são recriadas ao mudar a forma da fila e podem perder o vínculo com a peça; duas edições na mesma janela de 0,3 s em bancadas diferentes: a primeira da lista ganha. Ainda não há marca de "linkada" na janela Tarefas nem na lista de bancadas.
- **Fase 3 feita**: fila em lote (`Pausar/Retomar todas`, `Limpar concluídas`, `Limpar fila` com confirmação), presets de quantidade 5/10/25/50 nas opções da tarefa, confirmação ao salvar template com nome repetido.
- **Fase 3 ainda por fazer**: teclado na aba compacta; janela Tarefas (filtro "só travadas", busca "onde faço X?"); tooltip do template com a lista de tarefas; "enviar fila para outra bancada" (o link cobre o caso principal).

## Aba compacta: lista + detalhes (3/nov) - **testado no jogo, funcionando (relato do usuário, 3/nov)**
- A grade de ícones saiu. A aba (largura 920, opção `tabW` 700-1200) tem à esquerda a fila vanilla + busca + lista com nome; à direita o `RecipeDetail` (igual ao da janela: opções da tarefa, stats, materiais, ingredientes, fixar/comparar). Clique seleciona, "+" ou duplo clique adiciona, Shift+clique fixa, estrela favorita, botão direito abre a janela naquela receita.
- Refactor: o detalhe saiu de `Window_RecipePicker` para `RecipeDetail` (a janela deve se comportar igual; **testar regressão**: adicionar, fixar/comparar, seletor de munição, menu de ingrediente).
- (Já validado pelo usuário, inclusive a janela após o refactor.) Itens que foram o foco do teste: layout 920 px (detalhe com scroll dentro da aba), duplo clique, estoque/estrela na linha, memória de modo/quantidade da bancada.

## Entregue
- v1 (2/nov): gancho em `BillStack.DoListing`, janela única em tabela, CE por reflexão.
- v2 (3/nov): botão "Receitas" nas bancadas + `NiceCompat`; janela de 3 painéis; estoque de materiais; armadura por material; munição CE por calibre/classe/usuários e seletor de munição; grupos dinâmicos para "Outros"; comparar até 3; recentes; teclado; lembrança de grupo/ordenação por bancada (salva nas opções).

## Fase A, parte 1 (3/nov) - compila em 1.6 e 1.7, **não testado no jogo**
- Busca com várias palavras (AND) e prefixos `@` categoria, `%` camada/parte do corpo/tag, `#` mod; sem prefixo casa nome, mod, material, categoria, camada, tag, pesquisa. Botão `?` com a ajuda; Ctrl+F foca a busca.
- Colunas **Trabalho** (`WorkAmountTotal`), **Habilidade** (maior nível mínimo; vermelha se `SkillOk` falso) e **Valor** (MarketValue com material padrão), também para grupos mistos/"Outros"; seção "Produção" no detalhe (habilidades por nome, valor dos ingredientes no tooltip). Entram no comparar.
- Interpretação a confirmar com o usuário: "mínimo para cada item" = nível mínimo de habilidade.

## Confirmado em jogo (3/nov, relato do usuário)
Com a DLL das 11:46, **tudo funcionou**: aba compacta com grade de ícones, Ctrl+F, Shift+clique (pin), janela de receitas, opções da tarefa, fila e templates (o usuário não listou itens; tratar como validado no uso dele, sem log detalhado). O "beauty fixo" sumiu sozinho (não era do nosso código; provavelmente do Dubs Mint Menus, removido). Fechado pelo usuário.

## DLL instalada (3/nov 11:46)
`Bin/1.6` e `Bin/1.7` foram recompiladas e instaladas agora (build sem `-Out`). O teste anterior (aba sem grade, Ctrl+F e Shift+clique "sem efeito") foi feito com a DLL velha das 09:30, então não diz nada sobre o código novo. Re-testar tudo.

## Retorno do usuário e ajustes (3/nov) - compila em 1.6 e 1.7, **não testado**
- Relato: tudo funcionava; Dubs foi removido; o Nice só ganha por não abrir janela sempre.
- **Aba compacta** (`Patch_BillTab`, opção ligada por padrão, desliga sozinha com a aba do Nice ligada): a aba vanilla um pouco mais alta (620 px, ajustável), a fila vanilla em cima ("Adicionar tarefa" abre a nossa janela) e embaixo uma busca + grade de ícones das receitas; clique adiciona (com modo/quantidade lembrados da bancada), botão direito abre a janela já naquela receita. Queue encurtada por `ref Rect` no prefix de `DoListing`; o FillTab vanilla roda inteiro.
- **Ctrl+F**: não funcionava porque `QuickSearchWidget.Focus` só vale durante Repaint; agora levanta a flag `needFocus`. Não testado.
- **Shift+clique**: fixa a receita para o "Comparar" (painel de detalhe); a linha fixada mostra um pin. Era pouco visível; dica e tooltip atualizados.
- Opções agora rolam (ficaram longas).
- **Pendente**: "mostrar a beleza do local fixo" - nada no Bill Organizer/Mounted Arms mexe nisso (busca por beauty só achou stats de edifícios). Preciso de screenshot/descrição de onde aparece.

## Plano A-D completo (3/nov) - compila em 1.6 e 1.7, **nada testado no jogo**
Para testar de uma vez (pedir `Player.log` + screenshots):
1. **Fase A**: clicar num ingrediente do detalhe (ficha, achar no mapa, "fazer em uma bancada" cria bill RepeatCount na bancada com a fila mais curta); em roupas, chips Sharp/Blunt/Heat/Cold ordenam os materiais; Shift+clique na linha fixa para comparar; chip "Diferenças" no comparar (não persiste); opção de ocultar os ícones de ingredientes.
2. **Fase B**: seção "Opções da tarefa" no detalhe: modo (padrão / fazer X vezes / até ter X / para sempre) + quantidade (lembrados por bancada), "Tarefas" = adicionar N de uma vez, "Material" restringe o filtro de ingredientes (só desliga os outros stuffs; os demais ingredientes ficam), nome automático "Receita (Material)" (opção, desligada).
3. **Fase C**: chip "Fila (n)" no topo da janela troca o detalhe pela fila: arrastar para reordenar, pausar, copiar (área de transferência do jogo), duplicar, apagar, +/- (Shift x10, Ctrl x100), colar. **Não** mexi na fila da aba vanilla (Nice já tem a dele; o vanilla já copia/cola/reordena).
4. **Fase D**: "Salvar template"/"Templates" na fila; arquivo próprio `Config/BillOrganizer_Templates.xml` (carregado sob demanda com o Scribe do jogo, bills clonadas sem referências de save); colar pula receita indisponível e respeita o limite de 15 e avisa quantas pulou; renomear/apagar em "Gerenciar templates". Botão "Tarefas" na barra inferior (opção, desligada): lista bancadas por tipo, fila, ir à bancada, abrir receitas. Largura/altura da janela nas opções.
- Riscos conhecidos: o carregamento do arquivo de templates (`Scribe.loader.InitLoading` fora de save) é o ponto mais incerto - se o log mostrar "Could not read the bill templates", me mande. A tela de opções ficou mais comprida e pode cortar em tela pequena. Arrastar na fila depende de eventos IMGUI dentro de ScrollView (não visto em jogo). Sliders de largura/altura e o botão da barra só valem ao fechar as opções.
- Limites assumidos: "melhor material por stat" só para roupas (armas corpo a corpo por material não); ícones de trabalho/ingrediente do Dubs viraram a coluna Trabalho + opção dos ícones de ingrediente.

## Log de 3/nov (1ª carga no jogo)
- Carregou sem exceção nossa; o Nice Bill Tab estava ativo (mensagem informativa esperada). Aviso de textura corrigido. Ainda **sem teste da janela** (abrir bancada, botão, adicionar tarefa).

## Para testar (pedir `Player.log` e screenshot)
1. Nice Bill Tab **ligado**: aparece o botão "Receitas" na bancada, abre a janela, adicionar tarefa funciona e a aba do Nice continua intacta.
2. Nice **desligado** (caixinha no canto da aba dele): "Adicionar tarefa" abre a nossa janela.
3. Dubs ligado: sem erro.
4. TableMachining, ElectricSmithy, ElectricTailoringBench, FabricationBench, AmmoBench (CE), ElectricStove (VCE), DrugLab: grupos, colunas, estoque, armadura por material, munição por calibre, comparar.
5. Log sem "Mouse position stack", NRE ou aviso de transpiler.

## Riscos / pendências
- Layout (larguras, textos cortados) não visto: depende de screenshot.
- Estoque: compara em unidades da receita (volume ou nutrição) com `ResourceCounter`; ingredientes que não são recurso contado (armas, roupas para derreter) ficam "desconhecido".
- Armadura por material calculada na primeira vez que se abre um grupo de roupas (memoizada); pode dar um soluço em bancada com muita roupa.
- CE: a coluna de penetração mistura mm (CE) e fração (vanilla) por um critério global (`CeCompat.Active`).
- `Building.GetGizmos` e `ITab_Bills` podem mudar em versões futuras: tudo cai no menu original se o patch falhar.




